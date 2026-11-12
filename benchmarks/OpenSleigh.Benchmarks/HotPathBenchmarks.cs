using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Benchmarks;

/// <summary>
/// Per-message lookups on the consuming side: every message read from the outbox or from a broker
/// has its type resolved by name, and every handled message gets a new saga instance from the handler factory.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class HotPathBenchmarks
{
    private TypeResolver _typeResolver = null!;
    private string _firstRegisteredTypeName = null!;
    private string _laterRegisteredTypeName = null!;
    private IMessageHandlerFactory _handlerFactory = null!;
    private ISagaInstance<BenchmarkState> _sagaInstance = null!;

    [GlobalSetup]
    public void Setup()
    {
        _typeResolver = new TypeResolver();
        _typeResolver.Register(typeof(BenchmarkMessage));
        _typeResolver.Register(typeof(AnotherBenchmarkMessage));
        _firstRegisteredTypeName = typeof(BenchmarkMessage).FullName!;
        _laterRegisteredTypeName = typeof(AnotherBenchmarkMessage).FullName!;

        var services = new ServiceCollection().BuildServiceProvider();
        _handlerFactory = new DefaultMessageHandlerFactory(services);

        _sagaInstance = new SagaInstance<BenchmarkState>(
            instanceId: Guid.NewGuid().ToString(),
            triggerMessageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            descriptor: SagaDescriptor.Create<BenchmarkSaga, BenchmarkState>(),
            state: new BenchmarkState());
    }

    [Benchmark(Description = "TypeResolver.Resolve (first type registered)")]
    public Type? ResolveFirstRegisteredType()
        => _typeResolver.Resolve(_firstRegisteredTypeName);

    [Benchmark(Description = "TypeResolver.Resolve (later type, same assembly)")]
    public Type? ResolveLaterRegisteredType()
        => _typeResolver.Resolve(_laterRegisteredTypeName);

    [Benchmark(Description = "MessageHandlerFactory.Create")]
    public IHandleMessage<BenchmarkMessage> CreateHandler()
        => _handlerFactory.Create<BenchmarkMessage>(_sagaInstance);
}

public class AnotherBenchmarkMessage : IMessage { }
