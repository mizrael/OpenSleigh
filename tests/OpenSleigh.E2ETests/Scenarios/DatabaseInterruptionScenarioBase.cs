using System.Collections.Concurrent;
using System.Data.Common;
using System.Net.Sockets;
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
/// completed or dead-lettered, and every host to work again without being restarted.
/// </remarks>
public abstract class DatabaseInterruptionScenarioBase(ITestOutputHelper console) : ScenarioBase(console)
{
    /// <summary>how many invoices in a row have to complete for the hosts to count as working again.</summary>
    protected const int InvoicesInARowThatProveRecovery = 5;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PauseAfterARefusal = TimeSpan.FromMilliseconds(100);

    /// <summary>what became of the invoices of a scenario, and what its hosts did.</summary>
    protected sealed class Invoices
    {
        private readonly ConcurrentQueue<string> _issued = new();
        private readonly ConcurrentQueue<string> _completed = new();
        private int _refused;

        /// <summary>the handler runs of every host.</summary>
        public HandlerRuns Runs { get; } = new();

        public HostActivity Activity { get; } = new();

        /// <summary>invoices that got into the outbox.</summary>
        public IReadOnlyCollection<string> Issued => _issued;

        /// <summary>invoices the database did not take: nothing of them was stored.</summary>
        public int Refused => Volatile.Read(ref _refused);

        /// <summary>invoices whose saga is stored as completed.</summary>
        public IReadOnlyCollection<string> Completed => _completed;

        /// <summary>the runs of the handler that sends the invoice, in order.</summary>
        public IReadOnlyList<HandlerRun> SendsOf(string correlationId)
            => Runs.RunsOf<SendInvoice>().Where(handlerRun => handlerRun.CorrelationId == correlationId).ToArray();

        public void WasIssued(string correlationId) => _issued.Enqueue(correlationId);
        public void WasRefused() => Interlocked.Increment(ref _refused);
        public void WasCompleted(string correlationId) => _completed.Enqueue(correlationId);
    }

    protected static HostSetup CreateHostSetup(Invoices invoices, Action<IServiceCollection>? configureServices = null)
        => new(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services =>
            {
                services
                    .RecordHandlerRuns(invoices.Runs)
                    .RecordHostActivity(invoices.Activity);
                configureServices?.Invoke(services);
            }
        };

    /// <summary>
    /// what a host gives a message back with when its database is away: the waits of these scenarios go on past it,
    /// and stop at anything else.
    /// </summary>
    protected static bool IsDatabaseFailure(ProcessingFailure failure) => IsDatabaseFailure(failure.Exception);

    // the providers report a connection that was closed, or refused, as a DbException or as what the socket threw,
    // and Entity Framework wraps them
    private static bool IsDatabaseFailure(Exception exception)
        => exception is DbException or IOException or SocketException
           || exception is AggregateException aggregate && aggregate.InnerExceptions.Any(IsDatabaseFailure)
           || exception.InnerException is { } inner && IsDatabaseFailure(inner);

    /// <summary>
    /// how many times a host gave a message of the invoice back for another reason than a lock:
    /// such a message is dead-lettered.
    /// </summary>
    protected static int TimesGivenBack(ScenarioRun run, string correlationId)
        => run.ProcessingFailures.Count(failure => failure.CorrelationId == correlationId && !failure.IsLockConflict);

    /// <summary>
    /// issues one invoice through a host and follows it until its saga is stored as completed,
    /// or a host gives one of its messages back.
    /// </summary>
    /// <returns>true when the invoice completed.</returns>
    protected async Task<bool> IssueAnInvoiceAndFollowItAsync(ScenarioRun run, ScenarioHost host, Invoices invoices, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();

        if (!await TryPublishAsync(host, new IssueInvoice(correlationId), cancellationToken))
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
    protected static Task<bool> FollowInvoiceAsync(
        ScenarioRun run, Invoices invoices, string correlationId, int timesGivenBackBefore, CancellationToken cancellationToken)
    {
        async Task<bool> FollowAsync(CancellationToken following)
        {
            while (true)
            {
                if (await IsStoredAsCompletedAsync(run, correlationId, following))
                {
                    invoices.WasCompleted(correlationId);
                    return true;
                }

                if (TimesGivenBack(run, correlationId) > timesGivenBackBefore)
                    return false;

                await Task.Delay(PollingInterval, following);
            }
        }

        return run.WaitForAsync(
            $"invoice '{correlationId}' to be stored as completed, or one of its messages to be given back",
            FollowAsync, cancellationToken, IsDatabaseFailure);
    }

    /// <summary>
    /// publishes through a host, the way an application does, and tells whether the database took the message.
    /// </summary>
    protected async Task<bool> TryPublishAsync<TMessage>(ScenarioHost host, TMessage message, CancellationToken cancellationToken)
        where TMessage : IMessage
    {
        try
        {
            await host.PublishNewAsync(message, cancellationToken);
            return true;
        }
        catch (Exception e) when (IsDatabaseFailure(e))
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
        catch (Exception e) when (IsDatabaseFailure(e))
        {
            return false;
        }
    }

    /// <summary>
    /// issues invoices, one at a time and through each host in turn, until <see cref="InvoicesInARowThatProveRecovery"/>
    /// of them complete in a row and every host has done its part since this was called: taken an invoice into
    /// the outbox, relayed without leaving anything behind, and processed a message it was delivered.
    /// The hosts share their outbox and their queues: invoices that complete do not tell that every host works.
    /// </summary>
    protected Task IssueInvoicesUntilEveryHostWorksAgainAsync(ScenarioRun run, Invoices invoices, CancellationToken cancellationToken)
    {
        var hosts = run.Hosts.Where(host => host.IsRunning).ToArray();
        var tookAnInvoice = new HashSet<ScenarioHost>();
        invoices.Activity.StartOver();

        bool EveryHostWorksAgain()
            => hosts.All(host => tookAnInvoice.Contains(host)
                                 && invoices.Activity.HasRelayed(host)
                                 && invoices.Activity.HasProcessed(host));

        async Task IssueAsync(CancellationToken issuing)
        {
            var completedInARow = 0;

            for (var turn = 0; completedInARow < InvoicesInARowThatProveRecovery || !EveryHostWorksAgain(); turn++)
            {
                var host = hosts[turn % hosts.Length];
                if (await IssueAnInvoiceAndFollowItAsync(run, host, invoices, issuing))
                {
                    completedInARow++;
                    tookAnInvoice.Add(host);
                }
                else
                {
                    completedInARow = 0;
                }
            }
        }

        return run.WaitForAsync(
            $"{InvoicesInARowThatProveRecovery} invoices in a row to complete, and each of the {hosts.Length} hosts to take an invoice, " +
            "to relay and to process a message: the hosts to work again",
            IssueAsync, cancellationToken, IsDatabaseFailure);
    }

    /// <summary>
    /// every message a host gave back failed on the database, and each of them is in the dead letters, once.
    /// </summary>
    protected static Task AssertOnlyDatabaseFailuresWereDeadLetteredAsync(ScenarioRun run)
        => AssertDeadLettersAsync(run, IsDatabaseFailure);

    /// <summary>
    /// the same, and no invoice went missing or was sent twice: each one that got into the outbox completed,
    /// or a host gave one of its messages back.
    /// </summary>
    protected static async Task AssertEveryInvoiceIsAccountedForAsync(ScenarioRun run, Invoices invoices)
    {
        await AssertOnlyDatabaseFailuresWereDeadLetteredAsync(run);

        Assert.Equal(invoices.Completed.Count, invoices.Completed.Distinct().Count());

        var unaccountedFor = invoices.Issued
            .Except(invoices.Completed)
            .Where(correlationId => TimesGivenBack(run, correlationId) == 0);
        Assert.Empty(unaccountedFor);

        Assert.All(invoices.Completed, correlationId => Assert.Single(invoices.SendsOf(correlationId)));
    }
}
