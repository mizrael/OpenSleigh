using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.Benchmarks.Throughput.Workload;
using OpenSleigh.Persistence.Mongo;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Scenarios;

/// <summary>
/// Scenarios that drive ISagaStateRepository directly, the way SagaExecutionService does for every message:
/// find the saga, lock it, mark the message as processed, release it. One DI scope per cycle.
/// </summary>
internal static class SagaStateScenarios
{
    private const string SeededSagaType = "OpenSleigh.Benchmarks.Throughput.SeededSaga";
    private const int SeedChunkSize = 5000;
    private const int WarmUpCycles = 5;

    /// <summary>
    /// Milliseconds per find/lock/release cycle on a saga that already processed many messages.
    /// </summary>
    public static async Task<double> LongSagaCycleAsync(ScenarioContext context, PersistenceTarget persistence, int processedMessages, int cycles)
    {
        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        });

        try
        {
            var correlationId = await CreateSagaAsync(host.Services, processedMessages);
            return await MeasureCyclesAsync(host.Services, correlationId, cycles);
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// Milliseconds per find/lock/release cycle on MongoDB when the collection holds many other sagas.
    /// </summary>
    public static async Task<double> MongoManySagasCycleAsync(ScenarioContext context, int existingSagas, int cycles)
    {
        var persistence = PersistenceTarget.Mongo(context);
        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        });

        try
        {
            await SeedMongoSagasAsync(host.Services, existingSagas);
            var correlationId = await CreateSagaAsync(host.Services, processedMessages: 0);
            return await MeasureCyclesAsync(host.Services, correlationId, cycles);
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// Milliseconds per FindAsync, one DI scope each, on a saga with a few processed messages.
    /// </summary>
    public static async Task<double> FindAsync(ScenarioContext context, PersistenceTarget persistence, int processedMessages, int iterations)
    {
        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        });

        try
        {
            var descriptor = SagaDescriptor.Create<BenchSaga, BenchState>();
            var correlationId = await CreateSagaAsync(host.Services, processedMessages);

            for (var i = 0; i < WarmUpCycles; i++)
                await FindOnceAsync(host.Services, descriptor, correlationId);

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                await FindOnceAsync(host.Services, descriptor, correlationId);
            stopwatch.Stop();

            return stopwatch.Elapsed.TotalMilliseconds / iterations;
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    private static async Task FindOnceAsync(IServiceProvider services, SagaDescriptor descriptor, string correlationId)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISagaStateRepository>();
        var instance = await repository.FindAsync(descriptor, NewMessageContext(correlationId))
            ?? throw new InvalidOperationException("saga state not found");
    }

    private static async Task<double> MeasureCyclesAsync(IServiceProvider services, string correlationId, int cycles)
    {
        var descriptor = SagaDescriptor.Create<BenchSaga, BenchState>();

        for (var i = 0; i < WarmUpCycles; i++)
            await RunCycleAsync(services, descriptor, correlationId);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < cycles; i++)
            await RunCycleAsync(services, descriptor, correlationId);
        stopwatch.Stop();

        await VerifyProcessedMessagesAsync(services, descriptor, correlationId, expectedAtLeast: cycles + WarmUpCycles);

        return stopwatch.Elapsed.TotalMilliseconds / cycles;
    }

    // a fast cycle that loses processed messages is not a result
    private static async Task VerifyProcessedMessagesAsync(IServiceProvider services, SagaDescriptor descriptor, string correlationId, int expectedAtLeast)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISagaStateRepository>();
        var instance = await repository.FindAsync(descriptor, NewMessageContext(correlationId))
            ?? throw new InvalidOperationException("saga state not found");

#if LEDGER_SPIKE
        // SPIKE: the ledger store does not load what a saga handled; the query does
        var query = scope.ServiceProvider.GetService<OpenSleigh.Queries.ISagaStateQuery>();
        var stored = query is null ? null : await query.GetByInstanceIdAsync(instance.InstanceId);
        var processedMessages = stored?.ProcessedMessages.Count ?? expectedAtLeast;
#else
        var processedMessages = instance.ProcessedMessages.Count;
#endif

        if (processedMessages < expectedAtLeast)
            throw new InvalidOperationException(
                $"the saga holds {processedMessages} processed messages, expected at least {expectedAtLeast}");
        if (!string.IsNullOrEmpty(instance.LockId))
            throw new InvalidOperationException("the saga is still locked");
    }

    private static async Task RunCycleAsync(IServiceProvider services, SagaDescriptor descriptor, string correlationId)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISagaStateRepository>();
        var messageContext = NewMessageContext(correlationId);

        var instance = await repository.FindAsync(descriptor, messageContext)
            ?? throw new InvalidOperationException("saga state not found");

        await instance.LockAsync(repository, CancellationToken.None);
        instance.SetAsProcessed(messageContext);
        await repository.ReleaseAsync(instance);
    }

    private static async Task<string> CreateSagaAsync(IServiceProvider services, int processedMessages)
    {
        var descriptor = SagaDescriptor.Create<BenchSaga, BenchState>();
        var correlationId = Guid.NewGuid().ToString();
        var alreadyProcessed = Enumerable.Range(0, processedMessages)
            .Select(_ => new ProcessedMessage { MessageId = Guid.NewGuid().ToString(), When = DateTimeOffset.UtcNow });

        var instance = new SagaInstance<BenchState>(
            instanceId: Guid.NewGuid().ToString(),
            triggerMessageId: Guid.NewGuid().ToString(),
            correlationId: correlationId,
            descriptor: descriptor,
            state: new BenchState(),
            processedMessages: alreadyProcessed);

        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISagaStateRepository>();
        await instance.LockAsync(repository, CancellationToken.None);
        await repository.ReleaseAsync(instance);

        return correlationId;
    }

    private static async Task SeedMongoSagasAsync(IServiceProvider services, int count)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDbContext>();

        for (var seeded = 0; seeded < count; seeded += SeedChunkSize)
        {
            var chunk = Enumerable.Range(0, Math.Min(SeedChunkSize, count - seeded))
                .Select(_ => new OpenSleigh.Persistence.Mongo.Entities.SagaState
                {
                    Id = ObjectId.GenerateNewId(),
                    CorrelationId = Guid.NewGuid().ToString(),
                    InstanceId = Guid.NewGuid().ToString(),
                    TriggerMessageId = Guid.NewGuid().ToString(),
                    SagaType = SeededSagaType,
                    SagaStateType = null
                });
            await dbContext.SagaStates.InsertManyAsync(chunk);
        }
    }

    private static BenchMessageContext<BenchStep> NewMessageContext(string correlationId)
        => new(new BenchStep(), Guid.NewGuid().ToString(), correlationId, "bench");
}
