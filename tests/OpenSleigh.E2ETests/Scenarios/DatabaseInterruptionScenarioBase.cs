using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>how long to wait before asking again a database that has just refused.</summary>
    protected static readonly TimeSpan PauseAfterARefusal = TimeSpan.FromMilliseconds(100);

    /// <summary>what became of the invoices of a scenario.</summary>
    protected sealed class Invoices
    {
        private readonly ConcurrentDictionary<string, int> _timesSent = new();
        private readonly ConcurrentQueue<string> _completed = new();
        private int _issued;
        private int _refused;

        /// <summary>invoices that got into the outbox.</summary>
        public int Issued => Volatile.Read(ref _issued);

        /// <summary>invoices the database did not take: nothing of them was stored.</summary>
        public int Refused => Volatile.Read(ref _refused);

        /// <summary>invoices whose saga is stored as completed.</summary>
        public IReadOnlyCollection<string> Completed => _completed;

        /// <summary>how many times the handler that sends an invoice ran, for each invoice.</summary>
        public IReadOnlyDictionary<string, int> TimesSent => _timesSent;

        public void WasIssued() => Interlocked.Increment(ref _issued);
        public void WasRefused() => Interlocked.Increment(ref _refused);
        public void WasCompleted(string correlationId) => _completed.Enqueue(correlationId);
        public void WasSent(string correlationId) => _timesSent.AddOrUpdate(correlationId, 1, (_, times) => times + 1);
    }

    /// <param name="TimeToFirstCompletedInvoice">from the moment the scenario started issuing invoices again.</param>
    protected sealed record Recovery(TimeSpan TimeToFirstCompletedInvoice, TimeSpan TimeToSteadyState, int InvoicesTried);

    protected static HostSetup CreateHostSetup(
        Invoices invoices,
        WhileSendingInvoice? whileSending = null,
        Action<IServiceCollection>? configureServices = null)
    {
        Action<IMessageContext<SendInvoice>> onSent = context => invoices.WasSent(context.CorrelationId);

        return new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services =>
            {
                services
                    .AddSingleton(whileSending ?? ((_, _) => Task.CompletedTask))
                    .AddSingleton(onSent);
                configureServices?.Invoke(services);
            }
        };
    }

    /// <summary>the messages the hosts gave back for another reason than a lock: each of them is dead-lettered.</summary>
    protected static IReadOnlyCollection<ProcessingFailure> FailuresThatDeadLetter(ScenarioRun run)
        => run.ProcessingFailures.Where(failure => !failure.IsLockConflict).ToArray();

    /// <summary>
    /// issues one invoice and follows it until its saga is stored as completed, or a host gives one of its messages back.
    /// </summary>
    /// <returns>true when the invoice completed.</returns>
    protected async Task<bool> IssueAnInvoiceAndFollowItAsync(ScenarioRun run, Invoices invoices, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        var failuresBefore = FailuresThatDeadLetter(run).Count;

        if (!await TryPublishAsync(run, new IssueInvoice(correlationId), cancellationToken))
        {
            invoices.WasRefused();
            return false;
        }

        invoices.WasIssued();
        return await FollowInvoiceAsync(run, invoices, correlationId, failuresBefore, cancellationToken);
    }

    /// <summary>
    /// waits until the invoice is stored as completed, or a host gave a message back since <paramref name="failuresBefore"/>
    /// were counted: with one invoice on its way at a time, that message is one of its own.
    /// </summary>
    protected async Task<bool> FollowInvoiceAsync(
        ScenarioRun run, Invoices invoices, string correlationId, int failuresBefore, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (await IsStoredAsCompletedAsync(run, correlationId, cancellationToken))
            {
                invoices.WasCompleted(correlationId);
                return true;
            }

            if (FailuresThatDeadLetter(run).Count > failuresBefore)
                return false;

            await Task.Delay(PollingInterval, cancellationToken);
        }
    }

    /// <summary>
    /// publishes through a host, the way an application does, and tells whether the database took the message.
    /// </summary>
    protected async Task<bool> TryPublishAsync<TMessage>(ScenarioRun run, TMessage message, CancellationToken cancellationToken)
        where TMessage : IMessage
    {
        try
        {
            await run.AnyHost.PublishAsync(message, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
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
    protected async Task<Recovery> IssueInvoicesUntilTheyKeepCompletingAsync(ScenarioRun run, Invoices invoices, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan? firstCompleted = null;
        var tried = 0;
        var completedInARow = 0;

        while (completedInARow < InvoicesInARowThatProveRecovery)
        {
            tried++;
            if (await IssueAnInvoiceAndFollowItAsync(run, invoices, cancellationToken))
            {
                firstCompleted ??= elapsed.Elapsed;
                completedInARow++;
            }
            else
            {
                completedInARow = 0;
            }
        }

        return new Recovery(firstCompleted!.Value, elapsed.Elapsed, tried);
    }

    /// <summary>
    /// every message a host gave back failed on the database, and each of them is in the dead letters, once:
    /// nothing else is, and nothing was dropped on the way.
    /// </summary>
    protected static async Task AssertOnlyDatabaseFailuresWereDeadLetteredAsync(ScenarioRun run)
    {
        var failures = FailuresThatDeadLetter(run);
        Assert.All(failures, failure =>
        {
            Assert.False(failure.IsSagaNotFound, failure.Exception.ToString());
            Assert.False(failure.IsHandlerFailure, failure.Exception.ToString());
        });

        var expected = failures.GroupBy(failure => failure.MessageType)
            .ToDictionary(group => group.Key, group => group.LongCount());

        var deadLetters = await run.ReadDeadLettersAsync(CancellationToken.None);

        Assert.Equal(
            expected.OrderBy(entry => entry.Key.FullName),
            deadLetters.OrderBy(entry => entry.Key.FullName));
    }

    /// <summary>
    /// no invoice went missing, and none was sent twice: the ones that did not complete are the ones a host gave back.
    /// </summary>
    protected static void AssertEveryInvoiceIsAccountedFor(Invoices invoices, IReadOnlyCollection<ProcessingFailure> failures)
    {
        Assert.Equal(invoices.Completed.Count, invoices.Completed.Distinct().Count());
        Assert.True(invoices.Issued - invoices.Completed.Count <= failures.Count,
            $"{invoices.Issued} invoices were issued, {invoices.Completed.Count} completed and only {failures.Count} messages were given back.");

        Assert.All(invoices.Completed, correlationId => Assert.Equal(1, invoices.TimesSent.GetValueOrDefault(correlationId)));
    }
}
