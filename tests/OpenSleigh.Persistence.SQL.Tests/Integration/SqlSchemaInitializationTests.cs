using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Utils;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenSleigh.Persistence.SQL.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public abstract class SqlSchemaInitializationTests
{
    private readonly DbFixture _fixture;

    protected SqlSchemaInitializationTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    protected abstract void UsePersistence(IBusConfigurator configurator, SqlConfiguration configuration);

    [Fact]
    public async Task First_context_of_a_service_provider_should_create_the_database_and_its_schema()
    {
        var connectionString = await CreateMissingDatabaseAsync();

        await using var provider = BuildProvider(connectionString);

        Assert.False(await HasSagasAsync(provider));
    }

    [Fact]
    public async Task Schema_should_be_checked_once_per_service_provider()
    {
        var connectionString = await CreateMissingDatabaseAsync();

        await using (var provider = BuildProvider(connectionString))
        {
            Assert.False(await HasSagasAsync(provider));
            await DropDatabaseAsync(provider);

            // the same provider does not look again: this is what saves a round trip per context
            await Assert.ThrowsAnyAsync<Exception>(() => HasSagasAsync(provider));
        }

        // a new provider in the same process does, even for a database it has seen before
        await using var newProvider = BuildProvider(connectionString);
        Assert.False(await HasSagasAsync(newProvider));
    }

    [Fact]
    public async Task Instances_that_start_together_without_a_database_should_all_start_and_use_the_one_they_created()
    {
        var connectionString = await CreateMissingDatabaseAsync();

        var instances = Enumerable.Range(0, InstancesStartingTogether)
            .Select(_ => BuildProvider(connectionString))
            .ToArray();
        try
        {
            // released together: each one finds no database, or no tables, and creates what is missing
            using var start = new ManualResetEventSlim();
            var starting = instances
                .Select(instance => Task.Run(() =>
                {
                    start.Wait();
                    return HasSagasAsync(instance);
                }))
                .ToArray();
            start.Set();

            Assert.All(await Task.WhenAll(starting), hasSagas => Assert.False(hasSagas));

            await using var laterInstance = BuildProvider(connectionString);
            Assert.False(await HasSagasAsync(laterInstance));
            Assert.Empty(await ReadPendingAsync(laterInstance));
            Assert.Contains(Entities.OutboxMessage.DeliverAtIndexName, await ReadOutboxIndexNamesAsync(laterInstance));
        }
        finally
        {
            foreach (var instance in instances)
                await instance.DisposeAsync();
        }
    }

    private async Task<string> CreateMissingDatabaseAsync()
    {
        // the fixture creates the database and will remove it at the end: it is dropped here to start without one
        var (db, connectionString) = _fixture.CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        return connectionString;
    }

    #region Upgrade of a database created by the released version

    private const string ReleasedSchemasFolder = "Schemas";
    private const string OutboxTable = $"{Constants.DbSchema}.\"{Entities.OutboxMessage.TableName}\"";
    private const int InstancesStartingTogether = 8;

    private static readonly string[] TablesInDropOrder = ["SagaProcessedMessages", "SagaStates", Entities.OutboxMessage.TableName];

    /// <summary>
    /// the script EF Core generates for the model of the released version, stored with the tests.
    /// </summary>
    protected abstract string ReleasedSchemaScriptFileName { get; }

    /// <summary>
    /// a query returning the names of the indexes of the outbox table, in a column named Value.
    /// </summary>
    protected abstract string OutboxIndexNamesQuery { get; }

    /// <summary>
    /// creates a user that can read and write the tables but not change them, and returns its connection string.
    /// </summary>
    protected abstract Task<string> CreateUserWithoutSchemaPermissionsAsync(SagaDbContext admin, string connectionString);

    [Fact]
    public async Task A_database_with_the_released_schema_should_be_upgraded_and_its_pending_messages_relayed()
    {
        var released = await CreateDatabaseWithReleasedSchemaAsync();

        await using var provider = BuildProvider(released.ConnectionString);

        var pending = await ReadPendingAsync(provider);
        var message = Assert.Single(pending);
        Assert.Equal(released.PendingMessageId, message.MessageId);
        Assert.Null(message.DeliverAt);
    }

    [Fact]
    public async Task An_upgraded_database_should_store_and_hold_back_delayed_messages()
    {
        var released = await CreateDatabaseWithReleasedSchemaAsync();
        await using var provider = BuildProvider(released.ConnectionString);
        var systemInfo = provider.GetRequiredService<ISystemInfo>();
        var immediate = MessageEnvelope.Create(new FakeMessage(), systemInfo);
        var delayed = MessageEnvelope.Create(new FakeMessage(), systemInfo, TimeSpan.FromHours(1));

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().AppendAsync([delayed, immediate]);

        // an instance of the released version that is still running does not send a due time
        var storedByReleasedVersion = await InsertReleasedOutboxRowAsync(released.Admin);

        var pendingIds = (await ReadPendingAsync(provider)).Select(m => m.MessageId).OrderBy(id => id);
        Assert.Equal(
            new[] { released.PendingMessageId, immediate.MessageId, storedByReleasedVersion }.OrderBy(id => id),
            pendingIds);
    }

    [Fact]
    public async Task An_upgraded_database_should_have_the_outbox_table_of_a_new_one()
    {
        var released = await CreateDatabaseWithReleasedSchemaAsync();
        await using var upgradedProvider = BuildProvider(released.ConnectionString);
        var (_, newConnectionString) = _fixture.CreateDbContext();
        await using var newProvider = BuildProvider(newConnectionString);

        Assert.Equal(await DescribeOutboxColumnsAsync(newProvider), await DescribeOutboxColumnsAsync(upgradedProvider));
        Assert.Equal(await ReadOutboxIndexNamesAsync(newProvider), await ReadOutboxIndexNamesAsync(upgradedProvider));
        Assert.Contains(Entities.OutboxMessage.DeliverAtIndexName, await ReadOutboxIndexNamesAsync(upgradedProvider));
    }

    [Fact]
    public async Task The_upgrade_should_succeed_for_every_instance_when_several_start_together_and_when_one_starts_later()
    {
        var released = await CreateDatabaseWithReleasedSchemaAsync();
        var (_, newConnectionString) = _fixture.CreateDbContext();
        await using var newProvider = BuildProvider(newConnectionString);

        var instances = Enumerable.Range(0, InstancesStartingTogether)
            .Select(_ => BuildProvider(released.ConnectionString))
            .ToArray();
        try
        {
            await Task.WhenAll(instances.Select(instance => Task.Run(() => HasSagasAsync(instance))));

            await using var laterInstance = BuildProvider(released.ConnectionString);
            Assert.False(await HasSagasAsync(laterInstance));

            Assert.Equal(await DescribeOutboxColumnsAsync(newProvider), await DescribeOutboxColumnsAsync(laterInstance));
            Assert.Equal(await ReadOutboxIndexNamesAsync(newProvider), await ReadOutboxIndexNamesAsync(laterInstance));
            Assert.Equal(released.PendingMessageId, Assert.Single(await ReadPendingAsync(laterInstance)).MessageId);
        }
        finally
        {
            foreach (var instance in instances)
                await instance.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_user_that_cannot_change_the_schema_should_be_told_what_to_apply_and_should_work_once_it_is_applied()
    {
        var released = await CreateDatabaseWithReleasedSchemaAsync();
        var restrictedConnectionString = await CreateUserWithoutSchemaPermissionsAsync(released.Admin, released.ConnectionString);

        await using (var provider = BuildProvider(restrictedConnectionString))
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => HasSagasAsync(provider));
            var explanation = FindInvalidOperation(failure);
            Assert.NotNull(explanation);
            Assert.Contains("not allowed to change the schema", explanation.Message);
            Assert.Contains(nameof(Entities.OutboxMessage.DeliverAt), explanation.Message);

            // nothing was changed, and the same provider tries again when asked again
            await Assert.ThrowsAnyAsync<Exception>(() => HasSagasAsync(provider));
        }

        // someone who may change the schema applies the statements: here, an instance running as the owner
        await using (var ownerProvider = BuildProvider(released.ConnectionString))
            Assert.False(await HasSagasAsync(ownerProvider));

        await using var restrictedProvider = BuildProvider(restrictedConnectionString);
        Assert.False(await HasSagasAsync(restrictedProvider));
        Assert.Equal(released.PendingMessageId, Assert.Single(await ReadPendingAsync(restrictedProvider)).MessageId);
    }

    /// <summary>
    /// takes the lock that makes upgrades wait for each other, runs <paramref name="whileOthersWait"/>,
    /// applies the statements of the provider's upgrade, and lets go of the lock.
    /// </summary>
    protected abstract Task UpgradeWhileOthersWaitAsync(SagaDbContext admin, Func<Task> whileOthersWait);

    [Fact]
    public async Task An_instance_that_waited_for_another_one_to_upgrade_should_not_change_the_schema_again()
    {
        var released = await CreateDatabaseWithReleasedSchemaAsync();

        // an instance that cannot change the schema: if it tried to, it would fail
        var restrictedConnectionString = await CreateUserWithoutSchemaPermissionsAsync(released.Admin, released.ConnectionString);
        await using var waitingInstance = BuildProvider(restrictedConnectionString);
        Task<bool>? starting = null;

        await UpgradeWhileOthersWaitAsync(released.Admin, async () =>
        {
            starting = Task.Run(() => HasSagasAsync(waitingInstance));

            // long enough for it to find the upgrade missing and to queue behind the lock
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.False(starting.IsCompleted);
        });

        Assert.False(await starting!);
        Assert.Equal(released.PendingMessageId, Assert.Single(await ReadPendingAsync(waitingInstance)).MessageId);
    }

    private sealed record ReleasedDatabase(SagaDbContext Admin, string ConnectionString, string PendingMessageId);

    private async Task<ReleasedDatabase> CreateDatabaseWithReleasedSchemaAsync()
    {
        // the fixture creates the database with the current schema: its tables are replaced by the released ones
        var (admin, connectionString) = _fixture.CreateDbContext();
        foreach (var table in TablesInDropOrder)
            await admin.Database.ExecuteSqlRawAsync("DROP TABLE " + Constants.DbSchema + ".\"" + table + "\"");
        foreach (var statement in ReadReleasedSchemaStatements())
            await admin.Database.ExecuteSqlRawAsync(statement);

        var pendingMessageId = await InsertReleasedOutboxRowAsync(admin);
        return new ReleasedDatabase(admin, connectionString, pendingMessageId);
    }

    private IEnumerable<string> ReadReleasedSchemaStatements()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ReleasedSchemasFolder, ReleasedSchemaScriptFileName));

        // statements are separated by empty lines, and by GO in the SQL Server script
        return Regex.Split(script, @"(?:\r?\n){3,}|^GO\s*$", RegexOptions.Multiline)
            .Select(statement => statement.Trim())
            .Where(statement => statement.Length > 0);
    }

    // a row as the released version writes it: without a due time
    private static async Task<string> InsertReleasedOutboxRowAsync(SagaDbContext admin)
    {
        var messageId = Guid.NewGuid().ToString();

        await admin.Database.ExecuteSqlRawAsync(
            "INSERT INTO " + OutboxTable +
            " (\"MessageId\", \"CorrelationId\", \"Body\", \"MessageType\", \"CreatedAt\", \"SenderId\")" +
            " VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            messageId,
            Guid.NewGuid().ToString(),
            new JsonSerializer().Serialize(new FakeMessage()),
            typeof(FakeMessage).FullName!,
            DateTimeOffset.UtcNow,
            "sender");

        return messageId;
    }

    private static async Task<IReadOnlyCollection<MessageEnvelope>> ReadPendingAsync(IServiceProvider provider)
    {
        provider.GetRequiredService<ITypeResolver>().Register(typeof(FakeMessage));

        await using var relay = provider.CreateAsyncScope();
        await using var transaction = await relay.ServiceProvider.GetRequiredService<ITransactionManager>().StartTransactionAsync();
        var messages = await relay.ServiceProvider.GetRequiredService<IOutboxRepository>().ReadPendingAsync();
        return messages.ToArray();
    }

    private static async Task<IReadOnlyList<string>> DescribeOutboxColumnsAsync(IServiceProvider provider)
    {
        const string query =
            "SELECT CONCAT(column_name, ' ', data_type, ' ', is_nullable, ' ', column_default) AS \"Value\" " +
            "FROM information_schema.columns " +
            "WHERE table_schema = '" + Constants.DbSchema + "' AND table_name = '" + Entities.OutboxMessage.TableName + "'";

        return await QueryStringsAsync(provider, query);
    }

    private Task<IReadOnlyList<string>> ReadOutboxIndexNamesAsync(IServiceProvider provider)
        => QueryStringsAsync(provider, OutboxIndexNamesQuery);

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(IServiceProvider provider, string query)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        var values = await db.Database.SqlQueryRaw<string>(query).ToListAsync();
        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static InvalidOperationException? FindInvalidOperation(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is InvalidOperationException found)
                return found;

        return null;
    }

    #endregion

    private ServiceProvider BuildProvider(string connectionString)
        => new ServiceCollection()
            .AddOpenSleigh(configurator => UsePersistence(configurator, new SqlConfiguration(connectionString)))
            .BuildServiceProvider();

    private static async Task<bool> HasSagasAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        return await db.SagaStates.AnyAsync();
    }

    private static async Task DropDatabaseAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        await db.Database.EnsureDeletedAsync();
    }
}
