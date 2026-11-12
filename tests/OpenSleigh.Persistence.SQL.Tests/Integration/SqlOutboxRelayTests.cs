using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
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

    private ServiceProvider BuildProvider()
    {
        var (_, connectionString) = _fixture.CreateDbContext();
        var configuration = new SqlConfiguration(
            connectionString,
            SqlSagaStateRepositoryOptions.Default,
            SqlOutboxRepositoryOptions.Default with { MaxMessagesToPull = BatchSize });

        var provider = new ServiceCollection()
            .AddOpenSleigh(configurator => UsePersistence(configurator, configuration))
            .BuildServiceProvider();
        provider.GetRequiredService<ITypeResolver>().Register(typeof(FakeMessage));
        return provider;
    }

    private static async Task<IReadOnlyCollection<string>> AppendMessagesAsync(IServiceProvider provider, int count)
    {
        var systemInfo = provider.GetRequiredService<ISystemInfo>();
        var messages = Enumerable.Range(0, count)
            .Select(_ => MessageEnvelope.Create(new FakeMessage(), systemInfo))
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
