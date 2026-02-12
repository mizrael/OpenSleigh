using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Benchmarks;

/// <summary>
/// Measures pure dispatch overhead with zero allocations.
/// Each method accesses a typed property through a different dispatch mechanism,
/// then calls a no-op serializer. No object construction, no GC pressure.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class DispatchOverheadBenchmarks
{
    private ISagaInstance<BenchmarkState> _typedInstance = null!;
    private ISerializer _serializer = null!;
    private ISagaStateExtractor _extractor = null!;

    [GlobalSetup]
    public void Setup()
    {
        var descriptor = SagaDescriptor.Create<BenchmarkSaga, BenchmarkState>();
        _serializer = new NoOpSerializer();

        _typedInstance = new SagaInstance<BenchmarkState>(
            instanceId: Guid.NewGuid().ToString(),
            triggerMessageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            descriptor: descriptor,
            state: new BenchmarkState());

        _extractor = (ISagaStateExtractor)Activator.CreateInstance(
            typeof(SagaStateExtractor<>).MakeGenericType(descriptor.SagaStateType!))!;
    }

    [Benchmark(Baseline = true, Description = "Direct")]
    public byte[] Direct()
        => _serializer.Serialize(_typedInstance.State);

    [Benchmark(Description = "Dynamic")]
    public byte[] Dynamic()
        => ExtractDynamic((dynamic)_typedInstance, _serializer);

    [Benchmark(Description = "Wrapper")]
    public byte[] Wrapper()
        => _extractor.Extract(_typedInstance, _serializer);

    private static byte[] ExtractDynamic<TS>(ISagaInstance<TS> state, ISerializer serializer)
        => serializer.Serialize(state.State);
}

/// <summary>
/// Measures end-to-end saga instance creation including object construction.
/// SagaInstance allocates ~1000B (record + Dictionary + ConcurrentQueue),
/// which dominates the measurement. Dispatch overhead is a small fraction.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class SagaInstanceCreationBenchmarks
{
    private SagaDescriptor _descriptor = null!;
    private object _stateAsObject = null!;
    private string _instanceId = null!;
    private string _triggerMessageId = null!;
    private string _correlationId = null!;
    private ISagaStateInstanceCreator _creator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _descriptor = SagaDescriptor.Create<BenchmarkSaga, BenchmarkState>();
        _stateAsObject = new BenchmarkState();
        _instanceId = Guid.NewGuid().ToString();
        _triggerMessageId = Guid.NewGuid().ToString();
        _correlationId = Guid.NewGuid().ToString();

        _creator = (ISagaStateInstanceCreator)Activator.CreateInstance(
            typeof(SagaStateInstanceCreator<>).MakeGenericType(_descriptor.SagaStateType!))!;
    }

    [Benchmark(Baseline = true, Description = "Direct")]
    public ISagaInstance Direct()
        => new SagaInstance<BenchmarkState>(
            instanceId: _instanceId,
            triggerMessageId: _triggerMessageId,
            correlationId: _correlationId,
            descriptor: _descriptor,
            state: (BenchmarkState)_stateAsObject);

    [Benchmark(Description = "Dynamic")]
    public ISagaInstance Dynamic()
        => CreateDynamic(
            (dynamic)_stateAsObject, _instanceId,
            _triggerMessageId, _correlationId, _descriptor);

    [Benchmark(Description = "Wrapper")]
    public ISagaInstance Wrapper()
        => _creator.Create(
            _stateAsObject, _instanceId,
            _triggerMessageId, _correlationId, _descriptor);

    private static ISagaInstance CreateDynamic<TS>(
        TS state, string instanceId, string triggerMessageId,
        string correlationId, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
            instanceId: instanceId,
            triggerMessageId: triggerMessageId,
            correlationId: correlationId,
            descriptor: descriptor,
            state: state);
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
