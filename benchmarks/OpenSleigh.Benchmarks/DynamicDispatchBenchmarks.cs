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
            _descriptor);
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
