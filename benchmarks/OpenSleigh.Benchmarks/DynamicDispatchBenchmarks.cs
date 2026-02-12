using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class DynamicDispatchBenchmarks
{
    private SagaDescriptor _descriptor = null!;
    private object _stateAsObject = null!;
    private ISagaInstance<BenchmarkState> _typedInstance = null!;
    private ISerializer _serializer = null!;

    private string _instanceId = null!;
    private string _triggerMessageId = null!;
    private string _correlationId = null!;

    // Pre-warmed wrapper instances (not behind ConcurrentDictionary — measures pure dispatch)
    private ISagaStateInstanceCreator _creator = null!;
    private ISagaStateExtractor _extractor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _descriptor = SagaDescriptor.Create<BenchmarkSaga, BenchmarkState>();
        _stateAsObject = new BenchmarkState();
        _serializer = new NoOpSerializer();

        _instanceId = Guid.NewGuid().ToString();
        _triggerMessageId = Guid.NewGuid().ToString();
        _correlationId = Guid.NewGuid().ToString();

        _typedInstance = new SagaInstance<BenchmarkState>(
            instanceId: _instanceId,
            triggerMessageId: _triggerMessageId,
            correlationId: _correlationId,
            descriptor: _descriptor,
            state: new BenchmarkState());

        _creator = (ISagaStateInstanceCreator)Activator.CreateInstance(
            typeof(SagaStateInstanceCreator<>).MakeGenericType(_descriptor.SagaStateType!))!;
        _extractor = (ISagaStateExtractor)Activator.CreateInstance(
            typeof(SagaStateExtractor<>).MakeGenericType(_descriptor.SagaStateType!))!;
    }

    // --- Saga Instance Creation ---
    // All three use pre-generated strings — zero Guid noise, pure dispatch overhead.

    [Benchmark(Baseline = true, Description = "SagaInstance_Direct")]
    public ISagaInstance SagaInstanceCreation_Direct()
    {
        return new SagaInstance<BenchmarkState>(
            instanceId: _instanceId,
            triggerMessageId: _triggerMessageId,
            correlationId: _correlationId,
            descriptor: _descriptor,
            state: (BenchmarkState)_stateAsObject);
    }

    [Benchmark(Description = "SagaInstance_Dynamic")]
    public ISagaInstance SagaInstanceCreation_Dynamic()
    {
        return CreateDynamic(
            (dynamic)_stateAsObject, _instanceId,
            _triggerMessageId, _correlationId, _descriptor);
    }

    [Benchmark(Description = "SagaInstance_Wrapper")]
    public ISagaInstance SagaInstanceCreation_Wrapper()
    {
        return _creator.Create(
            _stateAsObject, _instanceId,
            _triggerMessageId, _correlationId, _descriptor);
    }

    // --- State Extraction ---
    // Pure dispatch overhead: no allocations in any path (NoOpSerializer returns empty array)

    [Benchmark(Description = "StateExtract_Direct")]
    public byte[] StateExtraction_Direct()
    {
        return _serializer.Serialize(_typedInstance.State);
    }

    [Benchmark(Description = "StateExtract_Dynamic")]
    public byte[] StateExtraction_Dynamic()
    {
        return ExtractDynamic((dynamic)_typedInstance, _serializer);
    }

    [Benchmark(Description = "StateExtract_Wrapper")]
    public byte[] StateExtraction_Wrapper()
    {
        return _extractor.Extract(_typedInstance, _serializer);
    }

    // --- Dynamic helpers (replicating current codebase behavior) ---

    private static ISagaInstance CreateDynamic<TS>(
        TS state, string instanceId, string triggerMessageId,
        string correlationId, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
            instanceId: instanceId,
            triggerMessageId: triggerMessageId,
            correlationId: correlationId,
            descriptor: descriptor,
            state: state);

    private static byte[] ExtractDynamic<TS>(ISagaInstance<TS> state, ISerializer serializer)
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
