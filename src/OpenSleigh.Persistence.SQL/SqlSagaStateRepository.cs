using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public record SqlSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly SqlSagaStateRepositoryOptions Default = new(TimeSpan.FromMinutes(1));
}

// SPIKE, not for merging: the ledger experiment behind the existing repository interfaces.
// A saga is a head row (state, version) and one ledger entry per handled message. Finding it reads the head and asks
// the ledger whether the message was handled already. Storing it is one statement that moves the head from the
// version that was found to the next, adds the entries and the outbox rows, or does nothing when the version moved.
// In lease mode a lease is written on the head before the handler, as the lock is today.
public class SqlSagaStateRepository : ISagaStateRepository, ISagaStateCommitter
{
    private const string NoLockId = "nolock";
    private const int SqlServerMaxRowsPerInsert = 500;

    private enum Dialect { Generic, PostgreSql, SqlServer }

    private sealed class Known
    {
        public int Version;
        public bool Stored;
        public string? ProcessedMessageId;
        public string? LeaseId;
    }

    private static readonly ConcurrentDictionary<Type, ILedgerInstanceFactory> _instanceFactories = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _stateExtractors = new();

    private readonly SagaDbContext _dbContext;
    private readonly SqlSagaStateRepositoryOptions _options;
    private readonly ISerializer _serializer;
    private readonly Dialect _dialect;
    private readonly string _sagas;
    private readonly string _ledger;
    private readonly string _outbox;

    // keyed by the instance itself: another message gets another instance, and nothing outlives its instance
    private readonly ConditionalWeakTable<ISagaInstance, Known> _known = new();

    public SqlSagaStateRepository(SagaDbContext dbContext, SqlSagaStateRepositoryOptions options, ISerializer serializer)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

        var provider = dbContext.Database.ProviderName ?? string.Empty;
        _dialect = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? Dialect.PostgreSql
            : provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ? Dialect.SqlServer
            : Dialect.Generic;

        var schema = _dialect == Dialect.Generic ? string.Empty : $"{Constants.DbSchema}.";
        _sagas = $"{schema}\"{SagaHead.TableName}\"";
        _ledger = $"{schema}\"{SagaLedgerEntry.TableName}\"";
        _outbox = $"{schema}\"{OutboxMessage.TableName}\"";
    }

    #region find

    public async ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        var stateType = descriptor.SagaStateType?.FullName;
        var stateTypeFilter = stateType is null ? "h.\"SagaStateType\" IS NULL" : "h.\"SagaStateType\" = @st";

        var sql =
            "SELECT h.\"InstanceId\", h.\"TriggerMessageId\", h.\"Version\", h.\"IsCompleted\", h.\"StateData\", " +
            $"CASE WHEN EXISTS (SELECT 1 FROM {_ledger} l WHERE l.\"InstanceId\" = h.\"InstanceId\" AND l.\"MessageId\" = @m) THEN 1 ELSE 0 END " +
            $"FROM {_sagas} h WHERE h.\"CorrelationId\" = @c AND h.\"SagaType\" = @t AND {stateTypeFilter}";

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand(sql);
        command.Add("@m", messageContext.MessageId);
        command.Add("@c", messageContext.CorrelationId);
        command.Add("@t", descriptor.SagaType.FullName!);
        if (stateType is not null)
            command.Add("@st", stateType);

        string instanceId, triggerMessageId;
        int version;
        bool isCompleted, processed;
        byte[]? stateData;

        await using (var reader = await command.Inner.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            instanceId = reader.GetString(0);
            triggerMessageId = reader.GetString(1);
            version = Convert.ToInt32(reader.GetValue(2));
            isCompleted = Convert.ToBoolean(reader.GetValue(3));
            stateData = reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4);
            processed = Convert.ToInt32(reader.GetValue(5)) == 1;
        }

        // the list of what the saga handled is not loaded: all that is known is whether it handled this message
        ProcessedMessage[] processedMessages = processed
            ? [new ProcessedMessage { MessageId = messageContext.MessageId, When = DateTimeOffset.UtcNow }]
            : [];

        ISagaInstance result;
        if (descriptor.SagaStateType is null)
            result = new SagaInstance(instanceId, triggerMessageId, messageContext.CorrelationId, descriptor, processedMessages);
        else
        {
            var state = stateData is null
                ? Activator.CreateInstance(descriptor.SagaStateType)
                : _serializer.Deserialize(stateData, descriptor.SagaStateType);
            var factory = _instanceFactories.GetOrAdd(descriptor.SagaStateType, static t =>
                (ILedgerInstanceFactory)Activator.CreateInstance(typeof(LedgerInstanceFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, instanceId, triggerMessageId, messageContext.CorrelationId, descriptor, processedMessages);
        }

        if (isCompleted)
            result.MarkAsCompleted();

        _known.AddOrUpdate(result, new Known
        {
            Version = version,
            Stored = true,
            ProcessedMessageId = processed ? messageContext.MessageId : null
        });

        return result;
    }

    #endregion find

    #region lock

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (LedgerSpike.Mode == LedgerConcurrencyMode.NoLock)
        {
            // nothing is written: the saga is stored as long as it is still at the version it was found at
            if (!_known.TryGetValue(state, out _))
                _known.AddOrUpdate(state, new Known());
            return ValueTask.FromResult(NoLockId);
        }

        return TakeLeaseAsync(state, cancellationToken);
    }

    private async ValueTask<string> TakeLeaseAsync(ISagaInstance state, CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        if (_known.TryGetValue(state, out var known) && known.Stored)
        {
            await using var take = connection.CreateCommand(
                $"UPDATE {_sagas} SET \"LeaseId\" = @l, \"LeaseTime\" = @now " +
                "WHERE \"InstanceId\" = @id AND \"Version\" = @v AND (\"LeaseTime\" IS NULL OR \"LeaseTime\" < @expired)");
            take.Add("@l", leaseId);
            take.Add("@now", now);
            take.Add("@id", state.InstanceId);
            take.Add("@v", known.Version);
            take.Add("@expired", now - _options.LockMaxDuration);

            if (await take.Inner.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            {
                known.LeaseId = leaseId;
                return leaseId;
            }

            // the saga is leased by someone else, or no longer as it was found: its version tells which
            await using var check = connection.CreateCommand($"SELECT \"Version\" FROM {_sagas} WHERE \"InstanceId\" = @id");
            check.Add("@id", state.InstanceId);
            var stored = await check.Inner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (stored is null || stored is DBNull || Convert.ToInt32(stored) != known.Version)
            {
                _known.Remove(state);
                throw new OptimisticLockException($"saga state '{state.InstanceId}' has changed since it was found");
            }

            throw new LockException($"saga state '{state.InstanceId}' is already locked");
        }

        // a new saga is stored with its lease, so that whoever finds it before its first message is done with waits
        await using var insert = connection.CreateCommand(
            $"INSERT INTO {_sagas} (\"InstanceId\", \"CorrelationId\", \"SagaType\", \"SagaStateType\", \"TriggerMessageId\", \"Version\", \"IsCompleted\", \"StateData\", \"LeaseId\", \"LeaseTime\") " +
            $"VALUES (@id, @c, @t, {Cast("@st", "text")}, @trigger, 0, @done, {Cast("@state", "bytea")}, @l, @now)");
        AddHeadParameters(insert, state, ExtractStateData(state));
        insert.Add("@l", leaseId);
        insert.Add("@now", now);

        try
        {
            await insert.Inner.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (IsUniqueViolation(ex))
        {
            throw new OptimisticLockException($"saga state for correlation '{state.CorrelationId}' was created by someone else");
        }

        _known.AddOrUpdate(state, new Known { Version = 0, Stored = true, LeaseId = leaseId });
        return leaseId;
    }

    #endregion lock

    #region store

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return AppendAsync(state, Array.Empty<MessageEnvelope>(), cancellationToken);
    }

    public ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return AppendAsync(state, state.Outbox, cancellationToken);
    }

    private async ValueTask AppendAsync(ISagaInstance state, IReadOnlyCollection<MessageEnvelope> outbox, CancellationToken cancellationToken)
    {
        if (!_known.TryGetValue(state, out var known))
            known = new Known();

        var entries = state.ProcessedMessages
            .Where(message => message.MessageId != known.ProcessedMessageId)
            .ToArray();

        // nothing was handled: nothing of the saga is stored, and its lease, if it has one, is given back
        if (entries.Length == 0)
        {
            await GiveLeaseBackAsync(state, known, cancellationToken).ConfigureAwait(false);
            _known.Remove(state);
            return;
        }

        var stateData = ExtractStateData(state);
        var messages = outbox.Select(message => OutboxMessage.Map(message, _serializer)).ToArray();

        bool stored;
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            stored = _dialect switch
            {
                Dialect.PostgreSql => await AppendOnPostgreSqlAsync(connection, state, known, entries, stateData, messages).ConfigureAwait(false),
                Dialect.SqlServer => await AppendOnSqlServerAsync(connection, state, known, entries, stateData, messages).ConfigureAwait(false),
                _ => await AppendStepByStepAsync(connection, state, known, entries, stateData, messages).ConfigureAwait(false),
            };
        }
        catch (DbException ex) when (IsUniqueViolation(ex))
        {
            // someone else stored the saga, or the same message, first
            stored = false;
        }

        _known.Remove(state);

        if (stored)
            return;

        LedgerSpike.CountAppendConflict();

        if (known.LeaseId is null)
            throw new OptimisticLockException($"saga state '{state.InstanceId}' has changed since it was found");

        throw new LockException($"unable to release Saga State '{state.InstanceId}' with lock id '{state.LockId}'");
    }

    private async ValueTask GiveLeaseBackAsync(ISagaInstance state, Known known, CancellationToken cancellationToken)
    {
        if (known.LeaseId is null)
            return;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand(
            $"UPDATE {_sagas} SET \"LeaseId\" = NULL, \"LeaseTime\" = NULL WHERE \"InstanceId\" = @id AND \"LeaseId\" = @l");
        command.Add("@id", state.InstanceId);
        command.Add("@l", known.LeaseId);
        await command.Inner.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private const string OutboxColumns =
        "\"CorrelationId\", \"Body\", \"MessageId\", \"MessageType\", \"CreatedAt\", \"SenderId\", \"DeliverAt\"";

    private string HeadStatement(Known known)
    {
        if (!known.Stored)
            return $"INSERT INTO {_sagas} (\"InstanceId\", \"CorrelationId\", \"SagaType\", \"SagaStateType\", \"TriggerMessageId\", \"Version\", \"IsCompleted\", \"StateData\") " +
                   $"VALUES (@id, @c, @t, {Cast("@st", "text")}, @trigger, @nv, @done, {Cast("@state", "bytea")})";

        var held = known.LeaseId is null ? string.Empty : " AND \"LeaseId\" = @lease";
        return $"UPDATE {_sagas} SET \"Version\" = @nv, \"IsCompleted\" = @done, \"StateData\" = {Cast("@state", "bytea")}, \"LeaseId\" = NULL, \"LeaseTime\" = NULL " +
               $"WHERE \"InstanceId\" = @id AND \"Version\" = @v{held}";
    }

    private void AddAppendParameters(SpikeCommand command, ISagaInstance state, Known known, int entries, byte[]? stateData)
    {
        if (known.Stored)
        {
            command.Add("@id", state.InstanceId);
            command.Add("@done", state.IsCompleted);
            command.Add("@state", stateData);
            command.Add("@v", known.Version);
            if (known.LeaseId is not null)
                command.Add("@lease", known.LeaseId);
        }
        else
            AddHeadParameters(command, state, stateData);

        command.Add("@nv", known.Version + entries);
    }

    private static void AddHeadParameters(SpikeCommand command, ISagaInstance state, byte[]? stateData)
    {
        command.Add("@id", state.InstanceId);
        command.Add("@c", state.CorrelationId);
        command.Add("@t", state.Descriptor.SagaType.FullName!);
        command.Add("@st", state.Descriptor.SagaStateType?.FullName);
        command.Add("@trigger", state.TriggerMessageId);
        command.Add("@done", state.IsCompleted);
        command.Add("@state", stateData);
    }

    private static void AddOutboxParameters(SpikeCommand command, int index, OutboxMessage message)
    {
        command.Add($"@o{index}c", message.CorrelationId);
        command.Add($"@o{index}b", message.Body);
        command.Add($"@o{index}m", message.MessageId);
        command.Add($"@o{index}t", message.MessageType);
        command.Add($"@o{index}a", message.CreatedAt);
        command.Add($"@o{index}s", message.SenderId);
        command.Add($"@o{index}d", message.DeliverAt);
    }

    // one statement: the entries and the outbox rows are only inserted when the head moved
    private async ValueTask<bool> AppendOnPostgreSqlAsync(
        SpikeConnection connection, ISagaInstance state, Known known, ProcessedMessage[] entries, byte[]? stateData, OutboxMessage[] messages)
    {
        var sql = new StringBuilder();
        sql.Append("WITH head AS (").Append(HeadStatement(known)).Append(" RETURNING 1), ");

        sql.Append($"entries AS (INSERT INTO {_ledger} (\"InstanceId\", \"Version\", \"MessageId\", \"OccurredAt\", \"StateData\") ")
           .Append("SELECT @id, x.v, x.m, x.t, CAST(@state AS bytea) FROM (VALUES ");
        for (var i = 0; i < entries.Length; i++)
            sql.Append(i == 0 ? string.Empty : ", ")
               .Append($"(CAST(@e{i}v AS integer), CAST(@e{i}m AS text), CAST(@e{i}t AS timestamptz))");
        sql.Append(") AS x(v, m, t) WHERE EXISTS (SELECT 1 FROM head) RETURNING 1)");

        if (messages.Length > 0)
        {
            sql.Append($", outbox AS (INSERT INTO {_outbox} ({OutboxColumns}) SELECT x.* FROM (VALUES ");
            for (var i = 0; i < messages.Length; i++)
                sql.Append(i == 0 ? string.Empty : ", ")
                   .Append($"(CAST(@o{i}c AS text), CAST(@o{i}b AS bytea), CAST(@o{i}m AS text), CAST(@o{i}t AS text), CAST(@o{i}a AS timestamptz), CAST(@o{i}s AS text), CAST(@o{i}d AS timestamptz))");
            sql.Append(") AS x WHERE EXISTS (SELECT 1 FROM head) ON CONFLICT DO NOTHING RETURNING 1)");
        }

        sql.Append(" SELECT count(*) FROM head");

        await using var command = connection.CreateCommand(sql.ToString());
        AddAppendParameters(command, state, known, entries.Length, stateData);
        AddEntryParameters(command, known, entries);
        for (var i = 0; i < messages.Length; i++)
            AddOutboxParameters(command, i, messages[i]);

        // not cancellable: cancelled in flight it can still be applied
        var moved = await command.Inner.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        return Convert.ToInt64(moved) == 1;
    }

    // one batch, one transaction, one round trip
    private async ValueTask<bool> AppendOnSqlServerAsync(
        SpikeConnection connection, ISagaInstance state, Known known, ProcessedMessage[] entries, byte[]? stateData, OutboxMessage[] messages)
    {
        var sql = new StringBuilder();
        sql.Append("SET XACT_ABORT ON; SET NOCOUNT ON; BEGIN TRAN; ")
           .Append(HeadStatement(known)).Append("; ")
           .Append("IF @@ROWCOUNT = 0 BEGIN ROLLBACK TRAN; SELECT 0; RETURN; END; ");

        for (var start = 0; start < entries.Length; start += SqlServerMaxRowsPerInsert)
        {
            sql.Append($"INSERT INTO {_ledger} (\"InstanceId\", \"Version\", \"MessageId\", \"OccurredAt\", \"StateData\") VALUES ");
            var end = Math.Min(entries.Length, start + SqlServerMaxRowsPerInsert);
            for (var i = start; i < end; i++)
                sql.Append(i == start ? string.Empty : ", ").Append($"(@id, @e{i}v, @e{i}m, @e{i}t, @state)");
            sql.Append("; ");
        }

        for (var i = 0; i < messages.Length; i++)
            sql.Append($"INSERT INTO {_outbox} ({OutboxColumns}) SELECT @o{i}c, @o{i}b, @o{i}m, @o{i}t, @o{i}a, @o{i}s, @o{i}d ")
               .Append($"WHERE NOT EXISTS (SELECT 1 FROM {_outbox} WHERE \"MessageId\" = @o{i}m); ");

        sql.Append("COMMIT TRAN; SELECT 1;");

        await using var command = connection.CreateCommand(sql.ToString());
        AddAppendParameters(command, state, known, entries.Length, stateData);
        AddEntryParameters(command, known, entries);
        for (var i = 0; i < messages.Length; i++)
            AddOutboxParameters(command, i, messages[i]);

        var moved = await command.Inner.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        return Convert.ToInt32(moved) == 1;
    }

    // for any other database (SQLite in the local benchmarks): the same writes, one statement each, in a transaction
    private async ValueTask<bool> AppendStepByStepAsync(
        SpikeConnection connection, ISagaInstance state, Known known, ProcessedMessage[] entries, byte[]? stateData, OutboxMessage[] messages)
    {
        await using var transaction = await connection.Inner.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);

        await using (var head = connection.CreateCommand(HeadStatement(known), transaction))
        {
            AddAppendParameters(head, state, known, entries.Length, stateData);
            if (await head.Inner.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false) == 0)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return false;
            }
        }

        for (var i = 0; i < entries.Length; i++)
        {
            await using var entry = connection.CreateCommand(
                $"INSERT INTO {_ledger} (\"InstanceId\", \"Version\", \"MessageId\", \"OccurredAt\", \"StateData\") VALUES (@id, @v, @m, @t, @state)",
                transaction);
            entry.Add("@id", state.InstanceId);
            entry.Add("@v", known.Version + i + 1);
            entry.Add("@m", entries[i].MessageId);
            entry.Add("@t", entries[i].When.ToUniversalTime());
            entry.Add("@state", stateData);
            await entry.Inner.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }

        for (var i = 0; i < messages.Length; i++)
        {
            await using var message = connection.CreateCommand(
                $"INSERT INTO {_outbox} ({OutboxColumns}) SELECT @o{i}c, @o{i}b, @o{i}m, @o{i}t, @o{i}a, @o{i}s, @o{i}d " +
                $"WHERE NOT EXISTS (SELECT 1 FROM {_outbox} WHERE \"MessageId\" = @o{i}m)",
                transaction);
            AddOutboxParameters(message, i, messages[i]);
            await message.Inner.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private static void AddEntryParameters(SpikeCommand command, Known known, ProcessedMessage[] entries)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            command.Add($"@e{i}v", known.Version + i + 1);
            command.Add($"@e{i}m", entries[i].MessageId);
            command.Add($"@e{i}t", entries[i].When.ToUniversalTime());
        }
    }

    #endregion store

    private string Cast(string parameter, string postgreSqlType)
        => _dialect == Dialect.PostgreSql ? $"CAST({parameter} AS {postgreSqlType})" : parameter;

    private byte[]? ExtractStateData(ISagaInstance state)
    {
        if (state.Descriptor.SagaStateType is null || !state.GetType().IsGenericType)
            return null;

        var extractor = _stateExtractors.GetOrAdd(state.Descriptor.SagaStateType, static t =>
            (ISagaStateExtractor)Activator.CreateInstance(
                typeof(SagaStateExtractor<>).MakeGenericType(t))!);
        return extractor.Extract(state, _serializer);
    }

    private static bool IsUniqueViolation(DbException exception)
    {
        if (exception.SqlState == "23505")
            return true;

        var type = exception.GetType();
        if (type.GetProperty("Number")?.GetValue(exception) is int number && number is 2627 or 2601)
            return true;

        return type.GetProperty("SqliteErrorCode")?.GetValue(exception) is int code && code == 19;
    }

    private async ValueTask<SpikeConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new SpikeConnection(_dbContext, _dialect == Dialect.SqlServer);
    }

    private sealed class SpikeConnection(SagaDbContext dbContext, bool sizedStrings) : IAsyncDisposable
    {
        public DbConnection Inner { get; } = dbContext.Database.GetDbConnection();

        public SpikeCommand CreateCommand(string sql, DbTransaction? transaction = null)
        {
            var command = Inner.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction ?? dbContext.Database.CurrentTransaction?.GetDbTransaction();
            return new SpikeCommand(command, sizedStrings);
        }

        public async ValueTask DisposeAsync()
            => await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
    }

    private sealed class SpikeCommand(DbCommand command, bool sizedStrings) : IAsyncDisposable
    {
        private const int SizedStringLength = 4000;

        public DbCommand Inner { get; } = command;

        public void Add(string name, string? value)
        {
            var parameter = Create(name, DbType.String, value);

            // one size for every value: SQL Server keeps one plan per parameter size
            if (sizedStrings)
                parameter.Size = value is null || value.Length <= SizedStringLength ? SizedStringLength : -1;
        }

        public void Add(string name, int value) => Create(name, DbType.Int32, value);
        public void Add(string name, bool value) => Create(name, DbType.Boolean, value);
        public void Add(string name, DateTimeOffset? value) => Create(name, DbType.DateTimeOffset, value?.ToUniversalTime());

        public void Add(string name, byte[]? value)
        {
            var parameter = Create(name, DbType.Binary, value);
            if (sizedStrings)
                parameter.Size = -1;
        }

        private DbParameter Create(string name, DbType type, object? value)
        {
            var parameter = Inner.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value ?? DBNull.Value;
            Inner.Parameters.Add(parameter);
            return parameter;
        }

        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}

internal interface ILedgerInstanceFactory
{
    ISagaInstance Create(
        object state, string instanceId, string triggerMessageId, string correlationId,
        SagaDescriptor descriptor, IEnumerable<ProcessedMessage> processedMessages);
}

internal sealed class LedgerInstanceFactory<TS> : ILedgerInstanceFactory
{
    public ISagaInstance Create(
        object state, string instanceId, string triggerMessageId, string correlationId,
        SagaDescriptor descriptor, IEnumerable<ProcessedMessage> processedMessages)
        => new SagaInstance<TS>(instanceId, triggerMessageId, correlationId, descriptor, (TS)state, processedMessages);
}
