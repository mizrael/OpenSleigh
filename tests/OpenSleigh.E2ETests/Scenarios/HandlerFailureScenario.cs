using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Queries;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a message handler that throws: the saga stays as it was, and the message is compensated or dead-lettered.
/// </summary>
public class HandlerFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);

    [Theory]
    [MatrixData]
    public async Task a_failed_message_without_compensation_is_dead_lettered_and_leaves_the_saga_as_it_was(string combination, int hostsCount)
    {
        var probe = new ShipmentProbe();
        var correlationId = Guid.NewGuid().ToString();
        SagaInstanceInfo? saga = null;
        IReadOnlyList<SagaMessageFailure> failures = [];

        await RunScenarioAsync(
            combination,
            hostsCount,
            SetupOf<ShipmentSaga>(probe),
            async (run, cancellationToken) =>
            {
                await StartShipmentAsync<ShipmentSaga>(run, correlationId, cancellationToken);

                await run.AnyHost.PublishAsync(new LoadShipment(correlationId), cancellationToken);

                failures = await WaitForFailuresAsync(run, correlationId, cancellationToken);
                saga = await run.WaitForSagaAsync<ShipmentSaga>(correlationId, stored => !stored.IsLocked, cancellationToken);
            },
            run => AssertDeadLettersAsync(run, typeof(LoadShipment)));

        AssertSagaIsAsItWasBeforeTheFailure(saga);

        var failure = Assert.Single(failures);
        Assert.Equal(FailureDecision.DeadLettered, failure.Decision);
        AssertDescribesTheFailedMessage(failure, saga!, typeof(ShipmentSaga));
        Assert.Null(failure.CompensationError);

        Assert.Equal(1, probe.LoadAttempts);
        Assert.Equal(0, probe.LoadedDeliveries);
        Assert.Equal(0, probe.Compensations);
    }

    [Theory]
    [MatrixData]
    public async Task a_failed_message_with_compensation_is_compensated_and_not_dead_lettered(string combination, int hostsCount)
    {
        var probe = new ShipmentProbe();
        var correlationId = Guid.NewGuid().ToString();
        SagaInstanceInfo? saga = null;
        IReadOnlyList<SagaMessageFailure> failures = [];

        await RunScenarioAsync(
            combination,
            hostsCount,
            SetupOf<CompensatingShipmentSaga>(probe),
            async (run, cancellationToken) =>
            {
                await StartShipmentAsync<CompensatingShipmentSaga>(run, correlationId, cancellationToken);

                await run.AnyHost.PublishAsync(new LoadShipment(correlationId), cancellationToken);

                // the message the compensation published was delivered, to a saga that has what the compensation stored
                await probe.Cancelled.Reached.WaitAsync(cancellationToken);

                // the shipment, the compensated message and the cancellation
                saga = await run.WaitForSagaAsync<CompensatingShipmentSaga>(
                    correlationId, stored => stored.ProcessedMessages.Count == 3 && !stored.IsLocked, cancellationToken);
                failures = await WaitForFailuresAsync(run, correlationId, cancellationToken);
            });

        Assert.Empty(probe.Cancelled.Problems);
        Assert.Equal(
            [ShipmentSaga.Started, ShipmentSaga.Cancelled],
            Assert.IsType<ShipmentState>(saga!.StateData).Steps);
        Assert.False(saga.IsCompleted);

        var failure = Assert.Single(failures);
        Assert.Equal(FailureDecision.Compensated, failure.Decision);
        AssertDescribesTheFailedMessage(failure, saga, typeof(CompensatingShipmentSaga));

        Assert.Equal(1, probe.LoadAttempts);
        Assert.Equal(1, probe.Compensations);
        Assert.Equal(1, probe.Cancelled.Count);
        Assert.Equal(0, probe.LoadedDeliveries);
    }

    [Theory]
    [MatrixData]
    public async Task a_failed_compensation_is_dead_lettered_and_leaves_the_saga_as_it_was(string combination, int hostsCount)
    {
        var probe = new ShipmentProbe();
        var correlationId = Guid.NewGuid().ToString();
        SagaInstanceInfo? saga = null;
        IReadOnlyList<SagaMessageFailure> failures = [];

        await RunScenarioAsync(
            combination,
            hostsCount,
            SetupOf<CompensatingShipmentSaga>(probe),
            async (run, cancellationToken) =>
            {
                await StartShipmentAsync<CompensatingShipmentSaga>(run, correlationId, cancellationToken);

                await run.AnyHost.PublishAsync(new LoadShipment(correlationId, CompensationFails: true), cancellationToken);

                failures = await WaitForFailuresAsync(run, correlationId, cancellationToken);
                saga = await run.WaitForSagaAsync<CompensatingShipmentSaga>(correlationId, stored => !stored.IsLocked, cancellationToken);
            },
            run => AssertDeadLettersAsync(run, typeof(LoadShipment)));

        Assert.Empty(probe.Cancelled.Problems);
        AssertSagaIsAsItWasBeforeTheFailure(saga);

        var failure = Assert.Single(failures);
        Assert.Equal(FailureDecision.CompensationFailed, failure.Decision);
        AssertDescribesTheFailedMessage(failure, saga!, typeof(CompensatingShipmentSaga));
        Assert.Contains(CompensatingShipmentSaga.CompensationFailure, failure.CompensationError);

        Assert.Equal(1, probe.LoadAttempts);
        Assert.Equal(1, probe.Compensations);
        Assert.Equal(0, probe.Cancelled.Count);
        Assert.Equal(0, probe.LoadedDeliveries);
    }

    private static HostSetup SetupOf<TSaga>(ShipmentProbe probe) where TSaga : ShipmentSaga
        => new(bus => bus.AddSaga<TSaga, ShipmentState>())
        {
            ConfigureServices = services => services.AddSingleton(probe)
        };

    // the failed message is only published once the saga is stored and released: it is not about a saga that is late
    private static async Task StartShipmentAsync<TSaga>(ScenarioRun run, string correlationId, CancellationToken cancellationToken)
    {
        await run.AnyHost.PublishAsync(new StartShipment(correlationId), cancellationToken);
        await run.WaitForSagaAsync<TSaga>(
            correlationId, stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked, cancellationToken);
    }

    // on MongoDB the record is stored just before the saga is released: the saga is read once it is unlocked
    private static async Task<IReadOnlyList<SagaMessageFailure>> WaitForFailuresAsync(
        ScenarioRun run, string correlationId, CancellationToken cancellationToken)
    {
        while (true)
        {
            await using (var scope = run.AnyHost.Services.CreateAsyncScope())
            {
                var query = scope.ServiceProvider.GetRequiredService<ISagaFailureQuery>();
                var failures = await query.GetAllAsync(new SagaFailureQueryFilter { CorrelationId = correlationId }, cancellationToken);
                if (failures.Items.Count > 0)
                    return failures.Items;
            }

            await Task.Delay(PollingInterval, cancellationToken);
        }
    }

    private static void AssertSagaIsAsItWasBeforeTheFailure(SagaInstanceInfo? saga)
    {
        Assert.NotNull(saga);
        Assert.Equal([ShipmentSaga.Started], Assert.IsType<ShipmentState>(saga.StateData).Steps);
        Assert.False(saga.IsCompleted);
        Assert.False(saga.IsLocked);
        Assert.Single(saga.ProcessedMessages);
    }

    private static void AssertDescribesTheFailedMessage(SagaMessageFailure failure, SagaInstanceInfo saga, Type sagaType)
    {
        Assert.Equal(saga.InstanceId, failure.InstanceId);
        Assert.Equal(saga.CorrelationId, failure.CorrelationId);
        Assert.Equal(sagaType.FullName, failure.SagaType);
        Assert.Equal(typeof(LoadShipment).FullName, failure.MessageType);
        Assert.Equal(typeof(InvalidOperationException).FullName, failure.ExceptionType);
        Assert.Equal(ShipmentSaga.LoadFailure, failure.ExceptionMessage);
        Assert.Equal(1, failure.Attempt);
    }

    /// <summary>
    /// the message whose handler failed was dead-lettered once, by a handler failure, and nothing else was.
    /// </summary>
    private static async Task AssertDeadLettersAsync(ScenarioRun run, Type failedMessageType)
    {
        var failure = Assert.Single(run.ProcessingFailures, failure => !failure.IsLockConflict);
        Assert.True(failure.IsHandlerFailure);
        Assert.Equal(failedMessageType, failure.MessageType);

        if (!run.SupportsDeadLetters)
            return;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var deadLetter = Assert.Single(await run.ReadDeadLettersAsync(timeout.Token));
        Assert.Equal(failedMessageType, deadLetter.Key);
        Assert.Equal(1, deadLetter.Value);
    }
}
