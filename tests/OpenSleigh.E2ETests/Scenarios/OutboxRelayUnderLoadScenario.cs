using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// several hosts relay one outbox: a backlog that each of them has read part of before any publishes, then the
/// messages the sagas publish while handling it and the ones a second burst of requests appends.
/// </summary>
public class OutboxRelayUnderLoadScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // a backlog of 200 messages, of which a relay takes 10 at a time on the SQL stores: every relay is still
    // reading when the others are. 600 messages go through the outbox in all
    private const int TransfersCount = 200;
    private const int HostsCount = 3;

    // the relay is code of the store, which claims the messages of a pass and removes them: every store runs it,
    // on RabbitMQ. A transport only adds the publisher the relay calls: one combination has the one of Kafka
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [HostsCount])]
    public async Task every_message_is_relayed_once_by_hosts_that_share_an_outbox(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);
        var startLine = new RelayStartLine(hostsCount);
        var relayed = new ConcurrentQueue<Relayed>();
        var deliveries = new ConcurrentDictionary<string, int>();

        var setup = new HostSetup(bus => bus.AddSaga<TransferSaga, TransferState>())
        {
            ConfigureServices = services => workload.AddTo(services)
                .Intercept(provider => new RelayAtTheStartLine(startLine, HostOf(provider)))
                .Decorate<IPublisher>((provider, inner) => new RecordingPublisher(inner, HostOf(provider), relayed))
                .Decorate<IMessageProcessor>((_, inner) => new CountingProcessor(inner, deliveries))
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                // nothing is relayed until every request is in the outbox, then every host reads it before any publishes
                await workload.OpenAllAsync(run.Hosts, cancellationToken);
                Assert.Equal(TransfersCount, await run.CountOutboxMessagesAsync(cancellationToken));
                startLine.Open();

                await workload.WaitUntilAllAreDebitedAsync(run, cancellationToken);
                await workload.SettleAllAsync(run.Hosts, cancellationToken);
                await run.WaitUntilAsync($"the {workload.MessagesCount} messages to be processed",
                    () => deliveries.Count >= workload.MessagesCount, cancellationToken);
            },
            AssertOnlyLockConflictsAsync);

        workload.AssertEveryMessageWasHandledOnce();

        // a message that two relays read is published twice, and delivered twice: its saga would ignore the copy,
        // so it is counted before that
        Assert.Equal(workload.MessagesCount, relayed.Count);
        Assert.Equal(workload.MessagesCount, relayed.Select(message => message.MessageId).Distinct().Count());
        Assert.DoesNotContain(deliveries, delivery => delivery.Value != 1);

        // the backlog only: what a saga publishes later is first seen by the relay of the host that handled its message
        var backlogByHost = relayed.Where(message => message.MessageType == typeof(OpenTransfer))
            .GroupBy(message => message.Host)
            .ToDictionary(messages => messages.Key, messages => messages.Count());
        Console.WriteLine($"backlog relayed by host: {string.Join(", ", backlogByHost.Values.Order())}");
        Assert.Equal(TransfersCount, backlogByHost.Values.Sum());

        // a relay on a SQL store locks the few rows of its pass, and the others skip them: every relay had read rows
        // of its own before any of them published. On MongoDB a relay claims every message that is due at once,
        // and may leave the others none
        if (!Combinations.Uses(combination, Combinations.MongoDb))
            Assert.Equal(hostsCount, backlogByHost.Count);
    }

    private static string HostOf(IServiceProvider provider) => provider.GetRequiredService<ISystemInfo>().ClientId;

    private sealed record Relayed(string Host, string MessageId, Type MessageType);

    /// <summary>
    /// keeps the relays from reading the outbox until it is opened, and then keeps each of them from publishing
    /// what its first pass read until every relay has read: they hold messages of the same backlog at the same time.
    /// </summary>
    private sealed class RelayStartLine(int relaysCount)
    {
        private readonly ConcurrentDictionary<string, byte> _haveRead = new();
        private readonly TaskCompletionSource _everyRelayHasRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _isOpen;

        public bool IsOpen => _isOpen;

        public void Open() => _isOpen = true;

        public Task HasReadAsync(string relay)
        {
            _haveRead.TryAdd(relay, 0);
            if (_haveRead.Count >= relaysCount)
                _everyRelayHasRead.TrySetResult();

            return _everyRelayHasRead.Task;
        }
    }

    private sealed class RelayAtTheStartLine(RelayStartLine startLine, string relay) : OutboxRepositoryInterceptor
    {
        public override async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(IOutboxRepository repository, CancellationToken cancellationToken)
        {
            if (!startLine.IsOpen)
                return [];

            // read first: a relay that waited before reading could publish and remove its pass before another one reads
            var messages = await base.ReadPendingAsync(repository, cancellationToken);
            await startLine.HasReadAsync(relay).WaitAsync(cancellationToken);
            return messages;
        }
    }

    private sealed class RecordingPublisher(IPublisher inner, string host, ConcurrentQueue<Relayed> relayed) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await inner.PublishAsync(envelope, cancellationToken);
            relayed.Enqueue(new Relayed(host, envelope.MessageId, envelope.MessageType));
        }
    }

    /// <summary>counts the messages a host was given and processed, by id. One that has to be retried is counted when it succeeds.</summary>
    private sealed class CountingProcessor(IMessageProcessor inner, ConcurrentDictionary<string, int> deliveries) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);
            deliveries.AddOrUpdate(outboxMessage.MessageId, 1, (_, count) => count + 1);
        }
    }
}
