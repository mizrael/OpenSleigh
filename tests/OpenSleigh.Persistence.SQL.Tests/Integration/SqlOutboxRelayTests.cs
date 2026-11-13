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
