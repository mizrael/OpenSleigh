using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// messages that have no saga to go to: one whose saga was never started, and two whose saga is already over.
/// None of them runs a handler. The first is dead-lettered, once; the others are ignored.
/// </summary>
public class MessageWithoutSagaScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int MessagesOfTheReservation = 2;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    // a pair of hosts shows what one would, and whether a message one host gave up on is given to the other
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [2])]
    public Task a_message_with_no_saga_to_go_to_runs_no_handler_on_any_host(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_with_no_saga_to_go_to_runs_no_handler_on_a_host_on_its_own(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    private async Task RunAsync(string combination, int hostsCount)
    {
        var neverReserved = Guid.NewGuid().ToString();
        var reservation = Guid.NewGuid().ToString();
        var runs = new HandlerRuns(MessagesOfTheReservation);
        var deliveries = new Deliveries();

        var setup = new HostSetup(bus => bus.AddSaga<ReservationSaga>())
        {
            ConfigureServices = services => services.AddSingleton(runs).CountDeliveries(deliveries)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(new CancelReservation(neverReserved), cancellationToken);
                while (run.ProcessingFailures.Count == 0)
                    await Task.Delay(PollingInterval, cancellationToken);

                await run.AnyHost.PublishAsync(new ReserveSeat(reservation), cancellationToken);
                await run.WaitForSagaAsync<ReservationSaga>(reservation,
                    saga => saga.ProcessedMessages.Count == 1 && !saga.IsLocked, cancellationToken);

                await run.AnyHost.PublishAsync(new ConfirmReservation(reservation), cancellationToken);
                await run.WaitForSagaAsync<ReservationSaga>(reservation,
                    saga => saga.IsCompleted && !saga.IsLocked, cancellationToken);

                // a late cancellation for the reservation that is over, and the message that started it, sent again
                await run.AnyHost.PublishAsync(new CancelReservation(reservation), cancellationToken);
                await run.AnyHost.PublishAsync(new ReserveSeat(reservation), cancellationToken);

                await deliveries.WaitUntilProcessedAsync<CancelReservation>(1, cancellationToken);
                await deliveries.WaitUntilProcessedAsync<ReserveSeat>(2, cancellationToken);

                // the saga is as it was when it was over, and it was not started again
                var stored = await run.FindSagaAsync<ReservationSaga>(reservation, cancellationToken);
                Assert.NotNull(stored);
                Assert.True(stored.IsCompleted);
                Assert.False(stored.IsLocked);
                Assert.Equal(MessagesOfTheReservation, stored.ProcessedMessages.Count);

                Assert.Null(await run.FindSagaAsync<ReservationSaga>(neverReserved, cancellationToken));
            },
            async run =>
            {
                // ignored is not failed: the only message a host gave back is the one whose saga does not exist.
                // It was given up on at once: no host was given it a second time
                await AssertOnlySagaNotFoundDeadLettersAsync(run, typeof(CancelReservation));

                var failure = Assert.Single(run.ProcessingFailures);
                Assert.True(failure.IsSagaNotFound, failure.ToString());
            });

        Assert.Empty(runs.RunsOf<CancelReservation>());
        Assert.Single(runs.RunsOf<ReserveSeat>());
        Assert.Single(runs.RunsOf<ConfirmReservation>());
        Assert.All(runs.Runs, run => Assert.Equal(reservation, run.CorrelationId));
        Assert.Single(runs.Runs.Select(run => run.InstanceId).Distinct());
    }
}
