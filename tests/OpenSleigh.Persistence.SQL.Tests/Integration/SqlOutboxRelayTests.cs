using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Integration;

/// <summary>
/// what the outbox relay does, through the services the persistence registers:
/// one scope per pass, a transaction, a read, deletes, a commit.
/// </summary>
[Category("Integration")]
[Trait("Category", "Integration")]
public abstract class SqlOutboxRelayTests
{
    private const int BatchSize = 2;

    private readonly DbFixture _fixture;

    protected SqlOutboxRelayTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    protected abstract void UsePersistence(IBusConfigurator configurator, SqlConfiguration configuration);

    [Fact]
    public async Task Messages_read_by_a_relay_should_be_skipped_by_another_until_its_transaction_ends()
    {
        await using var provider = BuildProvider();
        var appended = await AppendMessagesAsync(provider, 2 * BatchSize);

        await using var firstRelay = provider.CreateAsyncScope();
        await using var firstTransaction = await StartTransactionAsync(firstRelay);
        var readByFirst = await ReadPendingIdsAsync(firstRelay);

        await using var secondRelay = provider.CreateAsyncScope();
        await using var secondTransaction = await StartTransactionAsync(secondRelay);
        var readBySecond = await ReadPendingIdsAsync(secondRelay);

        Assert.Equal(BatchSize, readByFirst.Count);
        Assert.Equal(BatchSize, readBySecond.Count);
        Assert.Empty(readByFirst.Intersect(readBySecond));
        Assert.Equal(appended.OrderBy(id => id), readByFirst.Concat(readBySecond).OrderBy(id => id));
    }

    [Fact]
    public async Task Messages_read_but_not_deleted_should_be_read_again_after_the_transaction_ends()
    {
        await using var provider = BuildProvider();
        await AppendMessagesAsync(provider, BatchSize);

        IReadOnlyCollection<string> readFirst;
        await using (var relay = provider.CreateAsyncScope())
        {
            await using var transaction = await StartTransactionAsync(relay);
            readFirst = await ReadPendingIdsAsync(relay);
            await transaction.RollbackAsync();
        }

        await using var nextRelay = provider.CreateAsyncScope();
        await using var nextTransaction = await StartTransactionAsync(nextRelay);
        var readAgain = await ReadPendingIdsAsync(nextRelay);

        Assert.Equal(readFirst.OrderBy(id => id), readAgain.OrderBy(id => id));
    }

    [Fact]
    public async Task Messages_deleted_in_a_committed_transaction_should_be_gone()
    {
        await using var provider = BuildProvider();
        await AppendMessagesAsync(provider, BatchSize);

        await using (var relay = provider.CreateAsyncScope())
        {
            await using var transaction = await StartTransactionAsync(relay);
            var repository = relay.ServiceProvider.GetRequiredService<IOutboxRepository>();
            foreach (var message in await repository.ReadPendingAsync())
                await repository.DeleteAsync(message);
            await transaction.CommitAsync();
        }

        await using var nextRelay = provider.CreateAsyncScope();
        Assert.Empty(await ReadPendingIdsAsync(nextRelay));
    }

    [Fact]
    public async Task A_pass_deleted_as_a_batch_in_a_committed_transaction_should_be_gone_and_leave_the_rest()
    {
        await using var provider = BuildProvider();
        var appended = await AppendMessagesAsync(provider, 2 * BatchSize);

        IReadOnlyCollection<string> deleted;
        await using (var relay = provider.CreateAsyncScope())
        {
            await using var transaction = await StartTransactionAsync(relay);
            deleted = await DeletePendingAsBatchAsync(relay);
            await transaction.CommitAsync();
        }

        await using var nextRelay = provider.CreateAsyncScope();
        await using var nextTransaction = await StartTransactionAsync(nextRelay);
        var left = await ReadPendingIdsAsync(nextRelay);

        Assert.Equal(BatchSize, deleted.Count);
        Assert.Equal(appended.Except(deleted).OrderBy(id => id), left.OrderBy(id => id));
    }

    [Fact]
    public async Task A_pass_deleted_as_a_batch_should_be_read_again_when_its_transaction_is_rolled_back()
    {
        await using var provider = BuildProvider();
        await AppendMessagesAsync(provider, BatchSize);

        IReadOnlyCollection<string> deleted;
        await using (var relay = provider.CreateAsyncScope())
        {
            await using var transaction = await StartTransactionAsync(relay);
            deleted = await DeletePendingAsBatchAsync(relay);
            await transaction.RollbackAsync();
        }

        await using var nextRelay = provider.CreateAsyncScope();
        await using var nextTransaction = await StartTransactionAsync(nextRelay);
        var readAgain = await ReadPendingIdsAsync(nextRelay);

        Assert.Equal(deleted.OrderBy(id => id), readAgain.OrderBy(id => id));
    }

    [Fact]
    public async Task Due_messages_should_be_shared_between_relays_when_messages_that_are_not_due_are_waiting()
    {
        await using var provider = BuildProvider();
        await AppendMessagesAsync(provider, 3 * BatchSize, NotDueDelay);
        var due = await AppendMessagesAsync(provider, 2 * BatchSize);

        await using var firstRelay = provider.CreateAsyncScope();
        await using var firstTransaction = await StartTransactionAsync(firstRelay);
        var readByFirst = await ReadPendingIdsAsync(firstRelay);

        await using var secondRelay = provider.CreateAsyncScope();
        await using var secondTransaction = await StartTransactionAsync(secondRelay);
        var readBySecond = await ReadPendingIdsAsync(secondRelay);

        await using var thirdRelay = provider.CreateAsyncScope();
        await using var thirdTransaction = await StartTransactionAsync(thirdRelay);
        var readByThird = await ReadPendingIdsAsync(thirdRelay);

        Assert.Equal(BatchSize, readByFirst.Count);
        Assert.Equal(BatchSize, readBySecond.Count);
        Assert.Equal(due.OrderBy(id => id), readByFirst.Concat(readBySecond).OrderBy(id => id));
        Assert.Empty(readByThird);
    }

    [Fact]
    public async Task The_relay_should_publish_the_due_messages_and_leave_the_others_in_the_outbox()
    {
        var publisher = new RecordingPublisher();
        await using var provider = BuildProvider(services => services.AddSingleton<IPublisher>(publisher));
        var notDue = await AppendMessagesAsync(provider, 2 * BatchSize, NotDueDelay);
        var due = await AppendMessagesAsync(provider, 2 * BatchSize + 1);

        OutboxRelayResult result;
        do
        {
            await using var relay = provider.CreateAsyncScope();
            result = await relay.ServiceProvider.GetRequiredService<IOutboxProcessor>().RelayPendingMessagesAsync();
            Assert.Equal(0, result.Failed);
        } while (result.Relayed > 0);

        Assert.Equal(due.OrderBy(id => id), publisher.PublishedIds.OrderBy(id => id));

        await using var scope = provider.CreateAsyncScope();
        var left = await scope.ServiceProvider.GetRequiredService<SagaDbContext>()
            .OutboxMessages.AsNoTracking().Select(e => e.MessageId).ToListAsync();
        Assert.Equal(notDue.OrderBy(id => id), left.OrderBy(id => id));
    }

    private static readonly TimeSpan NotDueDelay = TimeSpan.FromHours(1);

    [Fact]
    public async Task A_message_put_back_while_the_relay_that_published_it_still_holds_it_should_wait_for_the_relay_and_stay()
    {
        await using var provider = BuildProvider();
        await AppendMessagesAsync(provider, 1);
        var returnedAt = DateTimeOffset.UtcNow;

        Task putBack;
        await using (var relay = provider.CreateAsyncScope())
        {
            await using var transaction = await StartTransactionAsync(relay);
            var repository = relay.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var published = Assert.Single(await repository.ReadPendingAsync());

            // the broker returns the message before the pass ends
            putBack = PutBackAsync(provider, published.Returned(returnedAt, returnedAt + NotDueDelay));
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.False(putBack.IsCompleted);

            await ((IOutboxBatchRepository)repository).DeleteAsync([published]);
            await transaction.CommitAsync();
        }

        await putBack.WaitAsync(TimeSpan.FromSeconds(20));

        await using var reader = provider.CreateAsyncScope();
        var stored = await reader.ServiceProvider.GetRequiredService<SagaDbContext>().OutboxMessages.AsNoTracking().SingleAsync();
        Assert.NotNull(stored.DeliverAt);
        Assert.NotNull(stored.FirstReturnedAt);
    }

    [Fact]
    public async Task A_message_put_back_should_be_published_again_when_it_is_due_with_the_time_it_first_came_back()
    {
        await using var provider = BuildProvider();
        await AppendMessagesAsync(provider, 1);
        var firstReturnedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        MessageEnvelope published;
        await using (var relay = provider.CreateAsyncScope())
        {
            await using var transaction = await StartTransactionAsync(relay);
            var repository = relay.ServiceProvider.GetRequiredService<IOutboxRepository>();
            published = Assert.Single(await repository.ReadPendingAsync());
            await ((IOutboxBatchRepository)repository).DeleteAsync([published]);
            await transaction.CommitAsync();
        }

        // twice, as when two relays published it and each got it back: the later one stores it first
        var later = firstReturnedAt.AddMinutes(1);
        await PutBackAsync(provider, published.Returned(later, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await PutBackAsync(provider, published.Returned(firstReturnedAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await PutBackAsync(provider, published.Returned(later, DateTimeOffset.UtcNow.AddSeconds(-1)));

        await using var nextRelay = provider.CreateAsyncScope();
        await using var nextTransaction = await StartTransactionAsync(nextRelay);
        var readAgain = Assert.Single(await nextRelay.ServiceProvider.GetRequiredService<IOutboxRepository>().ReadPendingAsync());

        Assert.Equal(published.MessageId, readAgain.MessageId);
        Assert.NotNull(readAgain.FirstReturnedAt);
        Assert.Equal(firstReturnedAt.ToUnixTimeMilliseconds(), readAgain.FirstReturnedAt.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Messages_put_back_by_several_hosts_together_should_all_be_stored_once_with_the_earliest_time_they_first_came_back()
    {
        const int hosts = 8;
        const int messagesCount = 2 * BatchSize * 25;
        await using var provider = BuildProvider();
        var systemInfo = provider.GetRequiredService<ISystemInfo>();
        var messages = Enumerable.Range(0, messagesCount).Select(_ => MessageEnvelope.Create(new FakeMessage(), systemInfo)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var earliest = now.AddMinutes(-3);

        // none of the messages is in the outbox any more, each host got them back at another time and in another order
        await Task.WhenAll(Enumerable.Range(0, hosts).Select(host => Task.Run(async () =>
        {
            var returned = messages
                .Select(message => message.Returned(earliest.AddSeconds(host), now + NotDueDelay))
                .OrderBy(_ => Random.Shared.Next())
                .ToArray();

            await using var scope = provider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            await ((IOutboxReturnRepository)repository).PutBackAsync(returned);
        })));

        await using var reader = provider.CreateAsyncScope();
        var stored = await reader.ServiceProvider.GetRequiredService<SagaDbContext>().OutboxMessages.AsNoTracking().ToListAsync();
        Assert.Equal(messages.Select(message => message.MessageId).Order(), stored.Select(row => row.MessageId).Order());
        Assert.All(stored, row => Assert.Equal(earliest.ToUnixTimeMilliseconds(), row.FirstReturnedAt!.Value.ToUnixTimeMilliseconds()));
    }

    private static async Task PutBackAsync(IServiceProvider provider, MessageEnvelope returned)
    {
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        await Assert.IsAssignableFrom<IOutboxReturnRepository>(repository).PutBackAsync([returned]);
    }

    private sealed class RecordingPublisher : IPublisher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _publishedIds = new();

        public IReadOnlyCollection<string> PublishedIds => _publishedIds;

        public ValueTask PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        {
            _publishedIds.Enqueue(message.MessageId);
            return ValueTask.CompletedTask;
        }
    }

    // what the relay does with a pass: the repository it resolves deletes it with one call
    private static async Task<IReadOnlyCollection<string>> DeletePendingAsBatchAsync(AsyncServiceScope relay)
    {
        var repository = relay.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var batchRepository = Assert.IsAssignableFrom<IOutboxBatchRepository>(repository);
        var messages = (await repository.ReadPendingAsync()).ToArray();
        await batchRepository.DeleteAsync(messages);
        return messages.Select(message => message.MessageId).ToArray();
    }

    private ServiceProvider BuildProvider(Action<IServiceCollection>? configureServices = null)
    {
        var (_, connectionString) = _fixture.CreateDbContext();
        var configuration = new SqlConfiguration(
            connectionString,
            SqlSagaStateRepositoryOptions.Default,
            SqlOutboxRepositoryOptions.Default with { MaxMessagesToPull = BatchSize });

        var services = new ServiceCollection()
            .AddLogging()
            .AddOpenSleigh(configurator => UsePersistence(configurator, configuration));
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITypeResolver>().Register(typeof(FakeMessage));
        return provider;
    }

    private static async Task<IReadOnlyCollection<string>> AppendMessagesAsync(IServiceProvider provider, int count, TimeSpan delay = default)
    {
        var systemInfo = provider.GetRequiredService<ISystemInfo>();
        var messages = Enumerable.Range(0, count)
            .Select(_ => MessageEnvelope.Create(new FakeMessage(), systemInfo, delay))
            .ToArray();

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().AppendAsync(messages);
        return messages.Select(message => message.MessageId).ToArray();
    }

    private static ValueTask<ITransaction> StartTransactionAsync(AsyncServiceScope relay)
        => relay.ServiceProvider.GetRequiredService<ITransactionManager>().StartTransactionAsync();

    private static async Task<IReadOnlyCollection<string>> ReadPendingIdsAsync(AsyncServiceScope relay)
    {
        var repository = relay.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var messages = await repository.ReadPendingAsync();
        return messages.Select(message => message.MessageId).ToArray();
    }
}
