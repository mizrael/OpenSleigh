using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// what the scenarios about a database that goes away share: invoices issued one at a time, each followed
/// until it is known what became of it, which is how a scenario tells when the hosts work again
/// without waiting for a time.
/// </summary>
/// <remarks>
/// A host that finds its database gone while it handles a message gives the message back with the error of the
/// database, and such a message is dead-lettered, not retried: only a lock conflict is. So an interruption costs
/// messages, by design, and these scenarios do not expect otherwise. They expect every message to be accounted for,
/// completed or dead-lettered, and the hosts to work again without being restarted.
/// </remarks>
public abstract class DatabaseInterruptionScenarioBase(ITestOutputHelper console) : ScenarioBase(console)
{
    /// <summary>how many invoices in a row have to complete for the hosts to count as working again.</summary>
    protected const int InvoicesInARowThatProveRecovery = 5;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PauseAfterARefusal = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DeadLettersTimeout = TimeSpan.FromSeconds(30);

    /// <summary>what became of the invoices of a scenario.</summary>
    protected sealed class Invoices
    {
        private readonly ConcurrentDictionary<string, int> _timesGivenBack = new();
        private readonly ConcurrentQueue<string> _issued = new();
        private readonly ConcurrentQueue<string> _completed = new();
        private int _refused;

        /// <summary>the handler runs of every host.</summary>
        public HandlerRuns Runs { get; } = new();

        /// <summary>invoices that got into the outbox.</summary>
        public IReadOnlyCollection<string> Issued => _issued;

        /// <summary>invoices the database did not take: nothing of them was stored.</summary>
        public int Refused => Volatile.Read(ref _refused);

        /// <summary>invoices whose saga is stored as completed.</summary>
        public IReadOnlyCollection<string> Completed => _completed;

        /// <summary>
        /// how many times a host gave a message of the invoice back for another reason than a lock:
        /// such a message is dead-lettered.
        /// </summary>
        public int TimesGivenBack(string correlationId) => _timesGivenBack.GetValueOrDefault(correlationId);

        /// <summary>how many times the handler that sends the invoice ran.</summary>
        public int TimesSent(string correlationId)
            => Runs.RunsOf<SendInvoice>().Count(handlerRun => handlerRun.CorrelationId == correlationId);

        public void WasIssued(string correlationId) => _issued.Enqueue(correlationId);
        public void WasRefused() => Interlocked.Increment(ref _refused);
        public void WasCompleted(string correlationId) => _completed.Enqueue(correlationId);
        public void WasGivenBack(string correlationId) => _timesGivenBack.AddOrUpdate(correlationId, 1, (_, times) => times + 1);
    }

    protected static HostSetup CreateHostSetup(Invoices invoices, Action<IServiceCollection>? configureServices = null)
        => new(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services =>
            {
                services
                    .RecordHandlerRuns(invoices.Runs)
                    .ObserveFailedDeliveries((message, failure) =>
                    {
                        if (!failure.IsLockConflict())
                            invoices.WasGivenBack(message.CorrelationId);
                    });
                configureServices?.Invoke(services);
            }
        };

    /// <summary>
    /// what a host gives a message back with when its database is away: anything but a saga that is missing
    /// or a handler that threw, neither of which an interruption causes. The waits of these scenarios go on past it.
    /// </summary>
    protected static bool IsInfrastructureFailure(ProcessingFailure failure)
        => !failure.IsSagaNotFound && !failure.IsHandlerFailure;

    /// <summary>
    /// issues one invoice and follows it until its saga is stored as completed, or a host gives one of its messages back.
    /// </summary>
    /// <returns>true when the invoice completed.</returns>
    protected async Task<bool> IssueAnInvoiceAndFollowItAsync(ScenarioRun run, Invoices invoices, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();

        if (!await TryPublishAsync(run, new IssueInvoice(correlationId), cancellationToken))
        {
            invoices.WasRefused();
            return false;
        }

        invoices.WasIssued(correlationId);
        return await FollowInvoiceAsync(run, invoices, correlationId, timesGivenBackBefore: 0, cancellationToken);
    }

    /// <summary>
    /// waits until the invoice is stored as completed, or a host gave one of its messages back
    /// more times than <paramref name="timesGivenBackBefore"/>.
    /// </summary>
    protected static async Task<bool> FollowInvoiceAsync(
        ScenarioRun run, Invoices invoices, string correlationId, int timesGivenBackBefore, CancellationToken cancellationToken)
    {
        async Task<bool> FollowAsync()
        {
            while (true)
            {
                if (await IsStoredAsCompletedAsync(run, correlationId, cancellationToken))
                {
                    invoices.WasCompleted(correlationId);
                    return true;
                }

                if (invoices.TimesGivenBack(correlationId) > timesGivenBackBefore)
                    return false;

                await Task.Delay(PollingInterval, cancellationToken);
            }
        }

        var followed = FollowAsync();
        await run.WaitForAsync(
            $"invoice '{correlationId}' to be stored as completed, or one of its messages to be given back",
            followed, cancellationToken, IsInfrastructureFailure);
        return await followed;
    }

    /// <summary>
    /// publishes through a host, the way an application does, and tells whether the database took the message.
    /// </summary>
    protected async Task<bool> TryPublishAsync<TMessage>(ScenarioRun run, TMessage message, CancellationToken cancellationToken)
        where TMessage : IMessage
    {
        try
        {
            await run.AnyHost.PublishNewAsync(message, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException and not Xunit.Sdk.XunitException)
        {
            Console.WriteLine($"the database refused a {typeof(TMessage).Name}: {e.GetType().Name}: {e.Message}");
            await Task.Delay(PauseAfterARefusal, cancellationToken);
            return false;
        }
    }

    // the scenario reads the store through a host, whose connections can be the ones that were just dropped
    private static async Task<bool> IsStoredAsCompletedAsync(ScenarioRun run, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await run.FindSagaAsync<InvoiceSaga>(correlationId, cancellationToken);
            return invoice is { IsCompleted: true };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// issues invoices, one at a time, until <see cref="InvoicesInARowThatProveRecovery"/> of them complete in a row.
    /// </summary>
    protected async Task IssueInvoicesUntilTheyKeepCompletingAsync(ScenarioRun run, Invoices invoices, CancellationToken cancellationToken)
    {
        async Task IssueAsync()
        {
            var completedInARow = 0;

            while (completedInARow < InvoicesInARowThatProveRecovery)
            {
                if (await IssueAnInvoiceAndFollowItAsync(run, invoices, cancellationToken))
                    completedInARow++;
                else
                    completedInARow = 0;
            }
        }

        await run.WaitForAsync(
            $"{InvoicesInARowThatProveRecovery} invoices in a row to complete: the hosts to work again",
            IssueAsync(), cancellationToken, IsInfrastructureFailure);
    }

    /// <summary>
    /// every message a host gave back failed on the infrastructure, and each of them is in the dead letters, once:
    /// nothing else is, and nothing was dropped on the way.
    /// </summary>
    protected static async Task AssertOnlyInfrastructureFailuresWereDeadLetteredAsync(ScenarioRun run)
    {
        var failures = run.ProcessingFailures.Where(failure => !failure.IsLockConflict).ToArray();
        Assert.All(failures, failure => Assert.True(IsInfrastructureFailure(failure), failure.ToString()));

        var expected = failures.GroupBy(failure => failure.MessageType)
            .ToDictionary(group => group.Key, group => group.LongCount());

        using var timeout = new CancellationTokenSource(DeadLettersTimeout);
        var deadLetters = await run.ReadDeadLettersAsync(timeout.Token);

        Assert.Equal(
            expected.OrderBy(entry => entry.Key.FullName),
            deadLetters.OrderBy(entry => entry.Key.FullName));
    }

    /// <summary>
    /// no invoice went missing, and none was sent twice: each one that got into the outbox completed,
    /// or a host gave one of its messages back.
    /// </summary>
    protected static void AssertEveryInvoiceIsAccountedFor(Invoices invoices)
    {
        Assert.Equal(invoices.Completed.Count, invoices.Completed.Distinct().Count());

        var unaccountedFor = invoices.Issued
            .Except(invoices.Completed)
            .Where(correlationId => invoices.TimesGivenBack(correlationId) == 0);
        Assert.Empty(unaccountedFor);

        Assert.All(invoices.Completed, correlationId => Assert.Equal(1, invoices.TimesSent(correlationId)));
    }
}
