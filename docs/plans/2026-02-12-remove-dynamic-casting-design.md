# Remove Dynamic Casting - Design Document

**Issue:** [#103](https://github.com/mizrael/OpenSleigh/issues/103)
**Date:** 2026-02-12
**Branch:** `feature/remove-dynamic-casting`

## Problem

Six locations in the codebase use `(dynamic)` casts to resolve generic type parameters at runtime via the DLR. This causes:

- **Performance overhead**: DLR call site resolution on first call, per-site memory for cached call sites
- **Allocations**: DLR infrastructure creates boxing and call site objects
- **Debugging difficulty**: Stack traces through DLR dispatch are harder to read
- **No compile-time safety**: Errors surface only at runtime

### Affected Files

| File | Line | Pattern |
|------|------|---------|
| `MessageProcessor.cs` | 24 | `ToContext((dynamic)outboxMessage.Message, outboxMessage)` |
| `SagaInstanceFactory.cs` | 28 | `Create((dynamic)instance, messageContext, descriptor)` |
| `MongoSagaStateRepository.cs` | 83 | `CreateSagaContext((dynamic)state, entity, descriptor)` |
| `MongoSagaStateRepository.cs` | 170 | `SetStateData((dynamic)state, entity)` |
| `SqlSagaStateRepository.cs` | 63 | `CreateSagaContext((dynamic)state, entity, descriptor)` |
| `SqlSagaStateRepository.cs` | 201 | `SetStateData((dynamic)state, entity)` |

## Solution: Wrapper Class Pattern

Inspired by [Nuntius Mediator](https://github.com/mizrael/Nuntius/blob/main/src/Nuntius/Mediator.cs), replace each `dynamic` cast with a **wrapper class** that encapsulates the generic dispatch behind a non-generic interface:

1. Define a non-generic interface for the operation
2. Implement it in a generic wrapper class that knows the concrete type
3. Cache wrapper instances in `ConcurrentDictionary<Type, TWrapper>`
4. `MakeGenericType` + `Activator.CreateInstance` happens once per type; all subsequent calls use fast interface dispatch

### Wrapper 1: MessageDispatcher (Core)

**Location:** `src/OpenSleigh/Transport/MessageDispatcher.cs`

```csharp
internal interface IMessageDispatcher
{
    ValueTask DispatchAsync(MessageEnvelope envelope, ISagaRunner runner,
                            SagaDescriptor descriptor, CancellationToken ct);
}

internal class MessageDispatcher<TM> : IMessageDispatcher where TM : IMessage
{
    public async ValueTask DispatchAsync(MessageEnvelope envelope, ISagaRunner runner,
                                         SagaDescriptor descriptor, CancellationToken ct)
    {
        var context = DefaultMessageContext<TM>.Create(envelope);
        await runner.ProcessAsync(context, descriptor, ct).ConfigureAwait(false);
    }
}
```

**Usage in `MessageProcessor`:**
```csharp
private static readonly ConcurrentDictionary<Type, IMessageDispatcher> _dispatchers = new();

var dispatcher = _dispatchers.GetOrAdd(outboxMessage.MessageType, t =>
    (IMessageDispatcher)Activator.CreateInstance(
        typeof(MessageDispatcher<>).MakeGenericType(t))!);

foreach (var descriptor in descriptors)
    await dispatcher.DispatchAsync(outboxMessage, _sagaRunner, descriptor, ct);
```

### Wrapper 2: SagaStateInstanceCreator (Core)

**Location:** `src/OpenSleigh/SagaStateInstanceCreator.cs`

```csharp
internal interface ISagaStateInstanceCreator
{
    ISagaInstance Create(object state, string triggerMessageId,
                         string correlationId, SagaDescriptor descriptor);
}

internal class SagaStateInstanceCreator<TS> : ISagaStateInstanceCreator
{
    public ISagaInstance Create(object state, string triggerMessageId,
                                string correlationId, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
#if NET9_0_OR_GREATER
            instanceId: Guid.CreateVersion7().ToString(),
#else
            instanceId: Guid.NewGuid().ToString(),
#endif
            triggerMessageId: triggerMessageId,
            correlationId: correlationId,
            descriptor: descriptor,
            state: (TS)state);
}
```

### Wrapper 3: SagaContextFactory (per persistence project)

**Mongo location:** `src/OpenSleigh.Persistence.Mongo/SagaContextFactory.cs`
**SQL location:** `src/OpenSleigh.Persistence.SQL/SagaContextFactory.cs`

```csharp
// Mongo version (SQL version is identical but uses SQL.Entities.SagaState)
internal interface ISagaContextFactory
{
    ISagaInstance Create(object state, Entities.SagaState entity, SagaDescriptor descriptor);
}

internal class SagaContextFactory<TS> : ISagaContextFactory
{
    public ISagaInstance Create(object state, Entities.SagaState entity, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
            instanceId: entity.InstanceId,
            triggerMessageId: entity.TriggerMessageId,
            correlationId: entity.CorrelationId,
            descriptor: descriptor,
            state: (TS)state,
            processedMessages: entity.ProcessedMessages.Select(e => new ProcessedMessage
            {
                MessageId = e.MessageId,
                When = e.When
            }));
}
```

### Wrapper 4: SagaStateExtractor (per persistence project)

**Mongo location:** Inline in `MongoSagaStateRepository.cs`
**SQL location:** Inline in `SqlSagaStateRepository.cs`

```csharp
internal interface ISagaStateExtractor
{
    byte[] Extract(ISagaInstance instance, ISerializer serializer);
}

internal class SagaStateExtractor<TS> : ISagaStateExtractor
{
    public byte[] Extract(ISagaInstance instance, ISerializer serializer)
        => serializer.Serialize(((ISagaInstance<TS>)instance).State);
}
```

**Usage replaces `SetStateData((dynamic)state, entity)`:**
```csharp
if (state.Descriptor.SagaStateType is not null)
{
    var extractor = _extractors.GetOrAdd(state.Descriptor.SagaStateType, t =>
        (ISagaStateExtractor)Activator.CreateInstance(
            typeof(SagaStateExtractor<>).MakeGenericType(t))!);
    entity.StateData = extractor.Extract(state, _serializer);
}
```

## Benchmarking Infrastructure

### Project structure

```
benchmarks/
├── OpenSleigh.Benchmarks/
│   ├── OpenSleigh.Benchmarks.csproj   # net9.0, BenchmarkDotNet
│   ├── Program.cs                      # BenchmarkRunner entry point
│   └── DynamicDispatchBenchmarks.cs    # Before/after comparison
└── results/                            # Committed Markdown summaries
```

### Benchmark scenarios

| Benchmark | What it measures |
|-----------|-----------------|
| `MessageDispatch_Dynamic` vs `_Wrapper` | IMessageContext creation + saga runner dispatch |
| `SagaInstanceCreation_Dynamic` vs `_Wrapper` | SagaInstance<TS> creation from runtime state type |
| `SagaStateExtraction_Dynamic` vs `_Wrapper` | Typed state extraction for serialization |

Each scenario includes a **direct generic call** baseline (compile-time dispatch).

### Metrics

- **Throughput** (ops/sec)
- **Allocations** (bytes/op)
- **Memory diagnoser** enabled

### Running

```bash
dotnet run -c Release --project benchmarks/OpenSleigh.Benchmarks/
```

Results output to `benchmarks/results/` with BenchmarkDotNet's Markdown + JSON exporters.

## Implementation Plan

### Step 1: Create feature branch
- Branch `feature/remove-dynamic-casting` from `develop`

### Step 2: Set up BenchmarkDotNet project
- Create `benchmarks/OpenSleigh.Benchmarks/` project
- Add BenchmarkDotNet package reference
- Write "before" benchmarks that capture current `dynamic` dispatch performance
- Run benchmarks and save baseline results

### Step 3: Implement Core wrappers
- Create `MessageDispatcher<TM>` in `src/OpenSleigh/Transport/`
- Update `MessageProcessor` to use wrapper dispatch
- Create `SagaStateInstanceCreator<TS>` in `src/OpenSleigh/`
- Update `SagaInstanceFactory` to use wrapper dispatch
- Run existing unit tests to verify no regressions

### Step 4: Implement Mongo persistence wrappers
- Create `SagaContextFactory<TS>` and `SagaStateExtractor<TS>` in `src/OpenSleigh.Persistence.Mongo/`
- Update `MongoSagaStateRepository` to use wrapper dispatch
- Verify build succeeds

### Step 5: Implement SQL persistence wrappers
- Create `SagaContextFactory<TS>` and `SagaStateExtractor<TS>` in `src/OpenSleigh.Persistence.SQL/`
- Update `SqlSagaStateRepository` to use wrapper dispatch
- Verify build succeeds

### Step 6: Write/update tests
- Add unit tests for each wrapper class
- Verify existing tests pass (unit + integration if Docker available)

### Step 7: Run "after" benchmarks
- Run the same benchmarks with the new wrapper implementation
- Save results alongside baseline for comparison
- Document the performance delta

### Step 8: Code review
- Run `superpowers:requesting-code-review` skill
- Address any issues

## Design Decisions

1. **Wrapper per persistence project** (not shared) — Entity types differ between Mongo and SQL, keeping wrappers project-local avoids cross-project dependencies
2. **Static `ConcurrentDictionary` caches** — Thread-safe, lock-free reads after initial population, shared across all instances
3. **`SagaStateType is not null` check** replaces `IsGenericType` — More semantic, expresses the actual business intent
4. **No public API changes** — All wrappers are `internal`, no changes to `ISagaInstance` or other public interfaces
