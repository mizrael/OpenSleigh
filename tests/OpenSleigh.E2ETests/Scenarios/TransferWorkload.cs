using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Queries;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a number of money transfers that are opened at the same time, for the scenarios about load:
/// what opens them, what their sagas report to, and what they must look like in the end.
/// </summary>
internal sealed class TransferWorkload
{
    // OpenTransfer, DebitAccount and CreditAccount
    public const int MessagesPerTransfer = 3;

    // a burst of requests, not one connection per transfer
    private const int PublishersAtOnce = 16;
    private const int ReadersAtOnce = 8;

    private readonly ConcurrentQueue<Guid> _completedTransfers = new();

    public TransferWorkload(int transfersCount)
    {
        Transfers = Enumerable.Range(1, transfersCount)
            .Select(number => new OpenTransfer(Guid.NewGuid(), Guid.NewGuid().ToString("N"), Amount: number))
            .ToArray();
        Completed = new Expectation(transfersCount);
    }

    /// <summary>each with an amount of its own: a transfer that was given the message of another does not add up.</summary>
    public IReadOnlyList<OpenTransfer> Transfers { get; }

    public Expectation Completed { get; }

    /// <summary>what a host that has <see cref="TransferSaga"/> needs in its services.</summary>
    public IServiceCollection AddTo(IServiceCollection services)
    {
        Action<IMessageContext<CreditAccount>> onCompleted = context =>
        {
            _completedTransfers.Enqueue(context.Message.TransferId);
            Completed.Signal();
        };

        return services.AddSingleton(onCompleted);
    }

    /// <summary>opens the transfers, a few at a time, each through one of the hosts in turn.</summary>
    public Task OpenAsync(IReadOnlyList<OpenTransfer> transfers, IReadOnlyList<ScenarioHost> publishers, CancellationToken cancellationToken)
        => Parallel.ForEachAsync(
            transfers.Select((transfer, index) => (transfer, publisher: publishers[index % publishers.Count])),
            new ParallelOptions { MaxDegreeOfParallelism = PublishersAtOnce, CancellationToken = cancellationToken },
            async (item, token) => await item.publisher.PublishAsync(item.transfer, token));

    public Task OpenAllAsync(IReadOnlyList<ScenarioHost> publishers, CancellationToken cancellationToken)
        => OpenAsync(Transfers, publishers, cancellationToken);

    /// <summary>every transfer was completed, and none of them twice.</summary>
    public void AssertEachCompletedOnce()
        => Assert.Equal(
            Transfers.Select(transfer => transfer.TransferId).Order(),
            _completedTransfers.Order());

    /// <summary>
    /// every transfer is stored as completed, with its own amount moved once and nothing of another transfer.
    /// </summary>
    public Task AssertEachIsStoredAsCompletedAsync(ScenarioRun run, CancellationToken cancellationToken)
        => Parallel.ForEachAsync(
            Transfers,
            new ParallelOptions { MaxDegreeOfParallelism = ReadersAtOnce, CancellationToken = cancellationToken },
            async (transfer, token) =>
            {
                // the handler reports before its saga is stored
                var saga = await run.WaitForSagaAsync<TransferSaga>(transfer.CorrelationId, IsCompletedAndReleased, token);

                var expected = new TransferState
                {
                    TransferId = transfer.TransferId,
                    Amount = transfer.Amount,
                    Debited = transfer.Amount,
                    Credited = transfer.Amount,
                    MessagesOfOtherTransfers = 0,
                };
                Assert.Equal(expected, Assert.IsType<TransferState>(saga.StateData));
                Assert.Equal(MessagesPerTransfer, saga.ProcessedMessages.Select(message => message.MessageId).Distinct().Count());
            });

    private static bool IsCompletedAndReleased(SagaInstanceInfo saga) => saga.IsCompleted && !saga.IsLocked;
}
