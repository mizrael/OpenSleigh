# Remove Dynamic Casting Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace all 6 `dynamic` casts with cached wrapper classes using the Nuntius pattern, add BenchmarkDotNet infrastructure.

**Architecture:** Each `dynamic` dispatch site gets a non-generic wrapper interface + generic wrapper class. Wrapper instances are cached in `ConcurrentDictionary<Type, TWrapper>` — `MakeGenericType` + `Activator.CreateInstance` happens once per type, then all calls go through fast interface dispatch.

**Tech Stack:** C# 12, .NET 8/9 multi-target, BenchmarkDotNet, xUnit + NSubstitute

**Design doc:** `docs/plans/2026-02-12-remove-dynamic-casting-design.md`

---

### Task 1: Create Feature Branch

**Step 1: Create and switch to feature branch**

Run: `cd /c/sources/prototypes/OpenSleigh && git checkout -b feature/remove-dynamic-casting develop`

Expected: `Switched to a new branch 'feature/remove-dynamic-casting'`

---

### Task 2: Set Up BenchmarkDotNet Project

**Files:**
- Create: `benchmarks/OpenSleigh.Benchmarks/OpenSleigh.Benchmarks.csproj`
- Create: `benchmarks/OpenSleigh.Benchmarks/Program.cs`
- Create: `benchmarks/OpenSleigh.Benchmarks/DynamicDispatchBenchmarks.cs`
- Modify: `.gitignore` — add `BenchmarkDotNet.Artifacts/`

**Step 1: Create the benchmark project directory**

Run: `mkdir -p /c/sources/prototypes/OpenSleigh/benchmarks/OpenSleigh.Benchmarks`

**Step 2: Create the benchmark csproj**

Create file `benchmarks/OpenSleigh.Benchmarks/OpenSleigh.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenSleigh\OpenSleigh.csproj" />
  </ItemGroup>

</Project>
```

Note: This project targets **net9.0 only** (not multi-target) since benchmarks are development-only tooling.

**Step 3: Create Program.cs**

Create file `benchmarks/OpenSleigh.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;
using OpenSleigh.Benchmarks;

BenchmarkRunner.Run<DynamicDispatchBenchmarks>();
```

**Step 4: Create the benchmark class**

Create file `benchmarks/OpenSleigh.Benchmarks/DynamicDispatchBenchmarks.cs`:

This benchmarks the three core dispatch patterns: message context creation, saga instance creation, and state extraction. Each compares `dynamic` vs wrapper vs direct generic call (baseline).

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;

namespace OpenSleigh.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class DynamicDispatchBenchmarks
{
    private BenchmarkMessage _message = null!;
    private SagaDescriptor _descriptor = null!;
    private object _stateAsObject = null!;
    private ISagaInstance<BenchmarkState> _typedInstance = null!;
    private ISerializer _serializer = null!;

    // Wrapper caches
    private static readonly ConcurrentDictionary<Type, ISagaStateInstanceCreator> _creators = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _extractors = new();

    [GlobalSetup]
    public void Setup()
    {
        _message = new BenchmarkMessage();
        _descriptor = SagaDescriptor.Create<BenchmarkSaga, BenchmarkState>();
        _stateAsObject = new BenchmarkState();
        _serializer = new NoOpSerializer();
        _typedInstance = new SagaInstance<BenchmarkState>(
            instanceId: Guid.NewGuid().ToString(),
            triggerMessageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            descriptor: _descriptor,
            state: new BenchmarkState());
    }

    // --- Saga Instance Creation ---

    [Benchmark(Description = "SagaInstance_Dynamic")]
    public ISagaInstance SagaInstanceCreation_Dynamic()
    {
        return CreateDynamic((dynamic)_stateAsObject, _descriptor);
    }

    [Benchmark(Description = "SagaInstance_Wrapper")]
    public ISagaInstance SagaInstanceCreation_Wrapper()
    {
        var creator = _creators.GetOrAdd(_descriptor.SagaStateType!, t =>
            (ISagaStateInstanceCreator)Activator.CreateInstance(
                typeof(SagaStateInstanceCreator<>).MakeGenericType(t))!);
        return creator.Create(_stateAsObject,
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), _descriptor);
    }

    [Benchmark(Baseline = true, Description = "SagaInstance_Direct")]
    public ISagaInstance SagaInstanceCreation_Direct()
    {
        return new SagaInstance<BenchmarkState>(
            instanceId: Guid.NewGuid().ToString(),
            triggerMessageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            descriptor: _descriptor,
            state: (BenchmarkState)_stateAsObject);
    }

    // --- State Extraction ---

    [Benchmark(Description = "StateExtract_Dynamic")]
    public byte[] StateExtraction_Dynamic()
    {
        return SetStateDataDynamic((dynamic)_typedInstance, _serializer);
    }

    [Benchmark(Description = "StateExtract_Wrapper")]
    public byte[] StateExtraction_Wrapper()
    {
        var extractor = _extractors.GetOrAdd(_descriptor.SagaStateType!, t =>
            (ISagaStateExtractor)Activator.CreateInstance(
                typeof(SagaStateExtractor<>).MakeGenericType(t))!);
        return extractor.Extract(_typedInstance, _serializer);
    }

    [Benchmark(Description = "StateExtract_Direct")]
    public byte[] StateExtraction_Direct()
    {
        return _serializer.Serialize(_typedInstance.State);
    }

    // --- Dynamic helpers (replicating current codebase behavior) ---

    private static ISagaInstance CreateDynamic<TS>(TS state, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
            instanceId: Guid.NewGuid().ToString(),
            triggerMessageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            descriptor: descriptor,
            state: state);

    private static byte[] SetStateDataDynamic<TS>(ISagaInstance<TS> state, ISerializer serializer)
        => serializer.Serialize(state.State);
}

// --- Benchmark support types ---

public class BenchmarkMessage : IMessage { }

public class BenchmarkState
{
    public int Counter { get; set; }
    public string Name { get; set; } = "benchmark";
}

public class BenchmarkSaga : Saga<BenchmarkState>, IStartedBy<BenchmarkMessage>
{
    public BenchmarkSaga(ISagaInstance<BenchmarkState> context) : base(context) { }

    public ValueTask HandleAsync(IMessageContext<BenchmarkMessage> messageContext,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

public class NoOpSerializer : ISerializer
{
    private static readonly byte[] Empty = Array.Empty<byte>();
    public byte[] Serialize(object data) => Empty;
    public object? Deserialize(ReadOnlySpan<byte> data, Type returnType) => null;
}

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddExporter(MarkdownExporter.GitHub);
    }
}
```

Note: The wrapper types `ISagaStateInstanceCreator`, `SagaStateInstanceCreator<TS>`, `ISagaStateExtractor`, `SagaStateExtractor<TS>` don't exist yet — they'll be created in Task 3. The benchmark project will only compile after Task 3.

**Step 5: Add BenchmarkDotNet.Artifacts to .gitignore**

Append to `.gitignore`:
```
BenchmarkDotNet.Artifacts/
```

**Step 6: Verify benchmark project restores**

Run: `cd /c/sources/prototypes/OpenSleigh && dotnet restore benchmarks/OpenSleigh.Benchmarks/OpenSleigh.Benchmarks.csproj`

Expected: Restore succeeds. Build will fail until wrapper types are created in Task 3.

**Step 7: Commit**

```
git add benchmarks/ .gitignore
git commit -m "feat: add BenchmarkDotNet infrastructure for dynamic dispatch profiling"
```

---

### Task 3: Implement Core Wrappers (MessageDispatcher + SagaStateInstanceCreator)

**Files:**
- Create: `src/OpenSleigh/Transport/MessageDispatcher.cs`
- Modify: `src/OpenSleigh/Transport/MessageProcessor.cs`
- Create: `src/OpenSleigh/SagaStateInstanceCreator.cs`
- Modify: `src/OpenSleigh/SagaInstanceFactory.cs`

**Step 1: Create MessageDispatcher wrapper**

Create file `src/OpenSleigh/Transport/MessageDispatcher.cs`:

```csharp
using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

internal interface IMessageDispatcher
{
    ValueTask DispatchAsync(
        MessageEnvelope envelope,
        ISagaRunner runner,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken);
}

internal class MessageDispatcher<TM> : IMessageDispatcher where TM : IMessage
{
    public async ValueTask DispatchAsync(
        MessageEnvelope envelope,
        ISagaRunner runner,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var context = DefaultMessageContext<TM>.Create(envelope);
        await runner.ProcessAsync(context, descriptor, cancellationToken)
                    .ConfigureAwait(false);
    }
}
```

**Step 2: Update MessageProcessor to use MessageDispatcher**

Replace the full content of `src/OpenSleigh/Transport/MessageProcessor.cs` with:

```csharp
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport;

internal class MessageProcessor : IMessageProcessor
{
    private static readonly ConcurrentDictionary<Type, IMessageDispatcher> _dispatchers = new();

    private readonly ISagaDescriptorsResolver _sagaDescriptorsResolver;
    private readonly ISagaRunner _sagaRunner;

    public MessageProcessor(
        ISagaRunner sagaRunner,
        ISagaDescriptorsResolver sagaDescriptorsResolver,
        ISerializer serializer)
    {
        _sagaRunner = sagaRunner ?? throw new ArgumentNullException(nameof(sagaRunner));
        _sagaDescriptorsResolver = sagaDescriptorsResolver ?? throw new ArgumentNullException(nameof(sagaDescriptorsResolver));
    }

    public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outboxMessage);

        var dispatcher = _dispatchers.GetOrAdd(outboxMessage.MessageType, static t =>
            (IMessageDispatcher)Activator.CreateInstance(
                typeof(MessageDispatcher<>).MakeGenericType(t))!);

        var descriptors = _sagaDescriptorsResolver.Resolve(outboxMessage.Message);
        foreach (var descriptor in descriptors)
        {
            try
            {
                await dispatcher.DispatchAsync(outboxMessage, _sagaRunner, descriptor, cancellationToken)
                                .ConfigureAwait(false);
            }
            catch (SagaException)
            {
                // TODO: send outboxMessage + descriptor to deadletter
            }
        }
    }
}
```

Key changes:
- Removed `ToContext` private method and its `(dynamic)` cast
- Added static `_dispatchers` cache
- `_dispatchers.GetOrAdd` creates `MessageDispatcher<TM>` on first access per message type
- Used `static` lambda to avoid closure allocation

**Step 3: Create SagaStateInstanceCreator wrapper**

Create file `src/OpenSleigh/SagaStateInstanceCreator.cs`:

```csharp
namespace OpenSleigh;

internal interface ISagaStateInstanceCreator
{
    ISagaInstance Create(
        object state,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor);
}

internal class SagaStateInstanceCreator<TS> : ISagaStateInstanceCreator
{
    public ISagaInstance Create(
        object state,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor)
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

**Step 4: Update SagaInstanceFactory to use wrapper**

Replace the full content of `src/OpenSleigh/SagaInstanceFactory.cs` with:

```csharp
using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh;

public class SagaInstanceFactory : ISagaInstanceFactory
{
    private static readonly ConcurrentDictionary<Type, ISagaStateInstanceCreator> _creators = new();

    public ISagaInstance Create<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext)
        where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(messageContext);

        if (descriptor.SagaStateType is null)
            return new SagaInstance(
#if NET9_0_OR_GREATER
                instanceId: Guid.CreateVersion7().ToString(),
#else
                instanceId: Guid.NewGuid().ToString(),
#endif
                triggerMessageId: messageContext.MessageId,
                correlationId: messageContext.CorrelationId,
                descriptor: descriptor);

        var instance = Activator.CreateInstance(descriptor.SagaStateType);
        if (instance is null)
            throw new TypeLoadException($"unable to create instance of type '{descriptor.SagaStateType.FullName}'");

        var creator = _creators.GetOrAdd(descriptor.SagaStateType, static t =>
            (ISagaStateInstanceCreator)Activator.CreateInstance(
                typeof(SagaStateInstanceCreator<>).MakeGenericType(t))!);

        return creator.Create(
            instance,
            messageContext.MessageId,
            messageContext.CorrelationId,
            descriptor);
    }
}
```

Key changes:
- Removed `Create<TS, TM>` private method and its `(dynamic)` cast
- Added static `_creators` cache
- Used `static` lambda to avoid closure allocation

**Step 5: Verify build succeeds**

Run: `cd /c/sources/prototypes/OpenSleigh/src && dotnet build --framework net9.0 OpenSleigh/OpenSleigh.csproj`

Expected: Build succeeded. 0 errors.

**Step 6: Run existing unit tests**

Run: `cd /c/sources/prototypes/OpenSleigh/src && dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"`

Expected: All ~115 tests pass.

**Step 7: Commit**

```
git add src/OpenSleigh/Transport/MessageDispatcher.cs src/OpenSleigh/Transport/MessageProcessor.cs src/OpenSleigh/SagaStateInstanceCreator.cs src/OpenSleigh/SagaInstanceFactory.cs
git commit -m "refactor: replace dynamic casts in MessageProcessor and SagaInstanceFactory with wrapper dispatch"
```

---

### Task 4: Implement Persistence Wrappers (Mongo + SQL)

**Files:**
- Create: `src/OpenSleigh.Persistence.Mongo/SagaContextFactory.cs`
- Create: `src/OpenSleigh.Persistence.Mongo/SagaStateExtractor.cs`
- Modify: `src/OpenSleigh.Persistence.Mongo/MongoSagaStateRepository.cs`
- Create: `src/OpenSleigh.Persistence.SQL/SagaContextFactory.cs`
- Create: `src/OpenSleigh.Persistence.SQL/SagaStateExtractor.cs`
- Modify: `src/OpenSleigh.Persistence.SQL/SqlSagaStateRepository.cs`

**Step 1: Create Mongo SagaContextFactory**

Create file `src/OpenSleigh.Persistence.Mongo/SagaContextFactory.cs`:

```csharp
namespace OpenSleigh.Persistence.Mongo;

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

**Step 2: Create Mongo SagaStateExtractor**

Create file `src/OpenSleigh.Persistence.Mongo/SagaStateExtractor.cs`:

```csharp
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo;

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

**Step 3: Update MongoSagaStateRepository**

In `src/OpenSleigh.Persistence.Mongo/MongoSagaStateRepository.cs`:

Add field after existing fields (around line 19):
```csharp
    private static readonly ConcurrentDictionary<Type, ISagaContextFactory> _contextFactories = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _stateExtractors = new();
```

Add using at the top:
```csharp
using System.Collections.Concurrent;
```

Replace line 83 (`result = CreateSagaContext((dynamic)state, entity, descriptor);`) with:
```csharp
            var factory = _contextFactories.GetOrAdd(descriptor.SagaStateType!, static t =>
                (ISagaContextFactory)Activator.CreateInstance(
                    typeof(SagaContextFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, entity, descriptor);
```

Replace lines 169-170 (`if (state.GetType().IsGenericType) SetStateData((dynamic)state, entity);`) with:
```csharp
        if (state.Descriptor.SagaStateType is not null)
        {
            var extractor = _stateExtractors.GetOrAdd(state.Descriptor.SagaStateType, static t =>
                (ISagaStateExtractor)Activator.CreateInstance(
                    typeof(SagaStateExtractor<>).MakeGenericType(t))!);
            entity.StateData = extractor.Extract(state, _serializer);
        }
```

Remove the now-unused private methods:
- `CreateSagaContext<TS>` (lines 31-42)
- `SetStateData<TS>` (lines 178-181)

**Step 4: Create SQL SagaContextFactory**

Create file `src/OpenSleigh.Persistence.SQL/SagaContextFactory.cs`:

```csharp
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL;

internal interface ISagaContextFactory
{
    ISagaInstance Create(object state, SagaState entity, SagaDescriptor descriptor);
}

internal class SagaContextFactory<TS> : ISagaContextFactory
{
    public ISagaInstance Create(object state, SagaState entity, SagaDescriptor descriptor)
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

**Step 5: Create SQL SagaStateExtractor**

Create file `src/OpenSleigh.Persistence.SQL/SagaStateExtractor.cs`:

```csharp
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.SQL;

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

**Step 6: Update SqlSagaStateRepository**

In `src/OpenSleigh.Persistence.SQL/SqlSagaStateRepository.cs`:

Add field after existing fields (around line 19):
```csharp
    private static readonly ConcurrentDictionary<Type, ISagaContextFactory> _contextFactories = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _stateExtractors = new();
```

Add using at the top:
```csharp
using System.Collections.Concurrent;
```

Replace line 63 (`result = CreateSagaContext((dynamic)state, entity, descriptor);`) with:
```csharp
            var factory = _contextFactories.GetOrAdd(descriptor.SagaStateType!, static t =>
                (ISagaContextFactory)Activator.CreateInstance(
                    typeof(SagaContextFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, entity, descriptor);
```

Replace lines 200-201 (`if (state.GetType().IsGenericType) SetStateData((dynamic)state, entity);`) with:
```csharp
        if (state.Descriptor.SagaStateType is not null)
        {
            var extractor = _stateExtractors.GetOrAdd(state.Descriptor.SagaStateType, static t =>
                (ISagaStateExtractor)Activator.CreateInstance(
                    typeof(SagaStateExtractor<>).MakeGenericType(t))!);
            entity.StateData = extractor.Extract(state, _serializer);
        }
```

Remove the now-unused private methods:
- `CreateSagaContext<TS>` (lines 72-83)
- `SetStateData<TS>` (lines 207-210)

**Step 7: Verify full solution builds**

Run: `cd /c/sources/prototypes/OpenSleigh/src && dotnet build --framework net9.0`

Expected: Build succeeded. 0 errors.

**Step 8: Verify no `dynamic` casts remain**

Run: `grep -rn '\bdynamic\b' src/ --include='*.cs' | grep -v obj/ | grep -v bin/`

Expected: No results.

**Step 9: Run unit tests**

Run: `cd /c/sources/prototypes/OpenSleigh/src && dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"`

Expected: All ~115 tests pass.

**Step 10: Commit**

```
git add src/OpenSleigh.Persistence.Mongo/SagaContextFactory.cs src/OpenSleigh.Persistence.Mongo/SagaStateExtractor.cs src/OpenSleigh.Persistence.Mongo/MongoSagaStateRepository.cs src/OpenSleigh.Persistence.SQL/SagaContextFactory.cs src/OpenSleigh.Persistence.SQL/SagaStateExtractor.cs src/OpenSleigh.Persistence.SQL/SqlSagaStateRepository.cs
git commit -m "refactor: replace dynamic casts in Mongo and SQL repositories with wrapper dispatch"
```

---

### Task 5: Write Unit Tests for Wrapper Classes

**Files:**
- Create: `tests/OpenSleigh.Tests/Transport/MessageDispatcherTests.cs`
- Create: `tests/OpenSleigh.Tests/SagaStateInstanceCreatorTests.cs`

**Step 1: Write MessageDispatcher tests**

Create file `tests/OpenSleigh.Tests/Transport/MessageDispatcherTests.cs`:

```csharp
using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Transport;

public class MessageDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_should_create_context_and_call_runner()
    {
        var message = new FakeSagaStarter();
        var sagaInstance = Substitute.For<ISagaInstance>();
        sagaInstance.CorrelationId.Returns(Guid.NewGuid().ToString());
        sagaInstance.InstanceId.Returns(Guid.NewGuid().ToString());
        var envelope = MessageEnvelope.Create(message, sagaInstance);
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var runner = Substitute.For<ISagaRunner>();

        var sut = new MessageDispatcher<FakeSagaStarter>();

        await sut.DispatchAsync(envelope, runner, descriptor, CancellationToken.None);

        await runner.Received(1).ProcessAsync(
            Arg.Is<IMessageContext<FakeSagaStarter>>(ctx =>
                ctx.MessageId == envelope.MessageId &&
                ctx.CorrelationId == envelope.CorrelationId),
            descriptor,
            Arg.Any<CancellationToken>());
    }
}
```

**Step 2: Write SagaStateInstanceCreator tests**

Create file `tests/OpenSleigh.Tests/SagaStateInstanceCreatorTests.cs`:

```csharp
namespace OpenSleigh.Tests;

public class SagaStateInstanceCreatorTests
{
    [Fact]
    public void Create_should_return_typed_saga_instance()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        var state = 42;

        var sut = new SagaStateInstanceCreator<int>();
        var result = sut.Create(state, "trigger-1", "corr-1", descriptor);

        Assert.NotNull(result);
        var typed = Assert.IsType<SagaInstance<int>>(result);
        Assert.Equal(42, typed.State);
        Assert.Equal("trigger-1", typed.TriggerMessageId);
        Assert.Equal("corr-1", typed.CorrelationId);
        Assert.Equal(descriptor, typed.Descriptor);
        Assert.NotEmpty(typed.InstanceId);
    }

    [Fact]
    public void Create_should_cast_state_from_object()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        object state = 99;

        var sut = new SagaStateInstanceCreator<int>();
        var result = sut.Create(state, "trigger-2", "corr-2", descriptor);

        var typed = Assert.IsType<SagaInstance<int>>(result);
        Assert.Equal(99, typed.State);
    }

    [Fact]
    public void Create_should_throw_on_invalid_cast()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        object state = "not an int";

        var sut = new SagaStateInstanceCreator<int>();

        Assert.Throws<InvalidCastException>(() =>
            sut.Create(state, "trigger-3", "corr-3", descriptor));
    }
}
```

**Step 3: Run new tests to verify they pass**

Run: `cd /c/sources/prototypes/OpenSleigh/src && dotnet test --framework net9.0 --filter "FullyQualifiedName~MessageDispatcherTests|FullyQualifiedName~SagaStateInstanceCreatorTests"`

Expected: All 4 tests pass.

**Step 4: Run full unit test suite**

Run: `cd /c/sources/prototypes/OpenSleigh/src && dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"`

Expected: All tests pass (original + 4 new).

**Step 5: Commit**

```
git add tests/OpenSleigh.Tests/Transport/MessageDispatcherTests.cs tests/OpenSleigh.Tests/SagaStateInstanceCreatorTests.cs
git commit -m "test: add unit tests for MessageDispatcher and SagaStateInstanceCreator wrappers"
```

---

### Task 6: Run Benchmarks and Save Results

**Step 1: Verify benchmark project builds**

Run: `cd /c/sources/prototypes/OpenSleigh && dotnet build -c Release benchmarks/OpenSleigh.Benchmarks/OpenSleigh.Benchmarks.csproj`

Expected: Build succeeded.

**Step 2: Run benchmarks**

Run: `cd /c/sources/prototypes/OpenSleigh && dotnet run -c Release --project benchmarks/OpenSleigh.Benchmarks/OpenSleigh.Benchmarks.csproj`

Expected: BenchmarkDotNet runs all scenarios and outputs Markdown table to console and `BenchmarkDotNet.Artifacts/` directory.

**Step 3: Copy results to tracked directory**

Run:
```
mkdir -p /c/sources/prototypes/OpenSleigh/benchmarks/results
cp BenchmarkDotNet.Artifacts/results/*-report-github.md benchmarks/results/
```

**Step 4: Commit results**

```
git add benchmarks/results/
git commit -m "perf: add benchmark results for dynamic vs wrapper dispatch"
```

---

### Task 7: Code Review

Run the `superpowers:requesting-code-review` skill to validate:
- All 6 `dynamic` casts are removed
- No `dynamic` keyword remains in production code
- Wrapper pattern is consistent across all sites
- Tests cover the new wrapper classes
- Benchmark results show improvement
- Build succeeds on both net8.0 and net9.0
- No public API changes
