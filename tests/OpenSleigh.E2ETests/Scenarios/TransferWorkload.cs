using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a number of money transfers that are opened at the same time, for the scenarios about load: what opens and
/// settles them, what records their handlers, and what they must look like in the store.
/// </summary>
internal sealed class TransferWorkload
{
    // OpenTransfer, DebitAccount and SettleTransfer
    public const int MessagesPerTransfer = 3;

    private const int MessagesHandledWhileInFlight = 2;

    // a burst of requests, not one connection per transfer
    private const int PublishersAtOnce = 16;
    private const int ReadersAtOnce = 8;

    public TransferWorkload(int transfersCount)
    {
        Transfers = Enumerable.Range(1, transfersCount)
            .Select(number => new OpenTransfer(Guid.NewGuid(), Guid.NewGuid().ToString("N"), Amount: number))
            .ToArray();
    }

    /// <summary>each with an amount of its own: a transfer that was given the message of another does not add up.</summary>
    public IReadOnlyList<OpenTransfer> Transfers { get; }

    public int MessagesCount => Transfers.Count * MessagesPerTransfer;

    public HandlerRuns Runs { get; } = new();

    /// <summary>what a host that has <see cref="TransferSaga"/> and does its work needs in its services.</summary>
    public IServiceCollection AddTo(IServiceCollection services) => services.RecordHandlerRuns(Runs);

    /// <summary>opens the transfers, a few at a time, each through one of the hosts in turn.</summary>
    public Task OpenAsync(IEnumerable<OpenTransfer> transfers, IReadOnlyList<ScenarioHost> publishers, CancellationToken cancellationToken)
        => PublishAsync(transfers, publishers, cancellationToken);

    public Task OpenAllAsync(IReadOnlyList<ScenarioHost> publishers, CancellationToken cancellationToken)
        => OpenAsync(Transfers, publishers, cancellationToken);

    public Task WaitUntilAllAreDebitedAsync(ScenarioRun run, CancellationToken cancellationToken)
        => run.WaitUntilAsync($"the {Transfers.Count} transfers to be debited",
            () => CountTransfersThatHandled<DebitAccount>() >= Transfers.Count, cancellationToken);

    public Task SettleAllAsync(IReadOnlyList<ScenarioHost> publishers, CancellationToken cancellationToken)
        => PublishAsync(
            Transfers.Select(transfer => new SettleTransfer(transfer.TransferId, transfer.CorrelationId, transfer.Amount)),
            publishers,
            cancellationToken);

    public Task WaitUntilAllAreSettledAsync(ScenarioRun run, CancellationToken cancellationToken)
        => run.WaitUntilAsync($"the {Transfers.Count} transfers to be settled",
            () => CountTransfersThatHandled<SettleTransfer>() >= Transfers.Count, cancellationToken);

    /// <summary>every transfer from the request that opens it to its settlement, all of them through the same hosts.</summary>
    public async Task RunToSettlementAsync(ScenarioRun run, IReadOnlyList<ScenarioHost> publishers, CancellationToken cancellationToken)
    {
        await OpenAllAsync(publishers, cancellationToken);
        await WaitUntilAllAreDebitedAsync(run, cancellationToken);
        await SettleAllAsync(publishers, cancellationToken);
        await WaitUntilAllAreSettledAsync(run, cancellationToken);
    }

    /// <summary>no handler ran twice for a message, and every transfer was settled.</summary>
    public void AssertEveryMessageWasHandledOnce()
    {
        Assert.All(Runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(MessagesCount, Runs.Runs.Count);
        Assert.Equal(
            Transfers.Select(transfer => transfer.CorrelationId).Order(),
            Runs.RunsOf<SettleTransfer>().Select(handlerRun => handlerRun.CorrelationId).Order());
    }

    /// <summary>
    /// every transfer is stored with its own amount moved once and nothing of another transfer:
    /// debited and waiting for its settlement, or settled and completed.
    /// </summary>
    public Task AssertEachIsStoredAsync(ScenarioRun run, bool settled, CancellationToken cancellationToken)
        => Parallel.ForEachAsync(
            Transfers,
            new ParallelOptions { MaxDegreeOfParallelism = ReadersAtOnce, CancellationToken = cancellationToken },
            async (transfer, token) =>
            {
                var saga = await run.WaitForReleasedSagaAsync<TransferSaga>(
                    transfer.CorrelationId, settled ? MessagesPerTransfer : MessagesHandledWhileInFlight, token);
                Assert.Equal(settled, saga.IsCompleted);

                var expected = new TransferState
                {
                    TransferId = transfer.TransferId,
                    Amount = transfer.Amount,
                    Debited = transfer.Amount,
                    Settled = settled ? transfer.Amount : 0,
                    MessagesOfOtherTransfers = 0,
                };
                Assert.Equal(expected, Assert.IsType<TransferState>(saga.StateData));
            });

    // by transfer, not by handler run: a handler that runs twice does not carry the count past what a wait expects,
    // and the wait ends for the assertions to say what went wrong
    private int CountTransfersThatHandled<TMessage>()
        where TMessage : IMessage
        => Runs.Runs.Where(handlerRun => handlerRun.MessageType == typeof(TMessage))
            .Select(handlerRun => handlerRun.CorrelationId)
            .Distinct()
            .Count();

    private static Task PublishAsync(IEnumerable<IMessage> messages, IReadOnlyList<ScenarioHost> publishers, CancellationToken cancellationToken)
        => Parallel.ForEachAsync(
            messages.Select((message, index) => (message, publisher: publishers[index % publishers.Count])),
            new ParallelOptions { MaxDegreeOfParallelism = PublishersAtOnce, CancellationToken = cancellationToken },
            async (item, token) => await item.publisher.PublishNewAsync(item.message, token));
}
