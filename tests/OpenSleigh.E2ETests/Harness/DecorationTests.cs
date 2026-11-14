using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Outbox;

namespace OpenSleigh.E2ETests.Harness;

[Trait("Category", ScenarioBase.E2ECategory)]
public class DecorationTests
{
    [Fact]
    public async Task an_intercepted_saga_state_repository_is_a_committer_exactly_when_the_original_is()
    {
        var plain = Substitute.For<ISagaStateRepository>();
        var committing = Substitute.For<ISagaStateRepository, ISagaStateCommitter>();
        var interceptor = new CountingInterceptor();
        var saga = Substitute.For<ISagaInstance>();

        Assert.IsNotAssignableFrom<ISagaStateCommitter>(Resolve(plain, services => services.Intercept(_ => interceptor)));

        var intercepted = Resolve(committing, services => services.Intercept(_ => interceptor));
        await intercepted.LockAsync(saga);
        await Assert.IsAssignableFrom<ISagaStateCommitter>(intercepted).CommitAsync(saga);

        Assert.Equal(1, interceptor.Commits);
        await committing.Received(1).LockAsync(saga, Arg.Any<CancellationToken>());
        await ((ISagaStateCommitter)committing).Received(1).CommitAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void an_intercepted_outbox_repository_deletes_in_batches_exactly_when_the_original_does()
    {
        var plain = Substitute.For<IOutboxRepository>();
        var batching = Substitute.For<IOutboxRepository, IOutboxBatchRepository>();

        Assert.IsNotAssignableFrom<IOutboxBatchRepository>(Resolve(plain, services => services.Intercept(_ => new OutboxRepositoryInterceptor())));
        Assert.IsAssignableFrom<IOutboxBatchRepository>(Resolve(batching, services => services.Intercept(_ => new OutboxRepositoryInterceptor())));
    }

    [Fact]
    public void a_decorator_that_hides_what_the_library_looks_for_is_refused()
    {
        var committing = Substitute.For<ISagaStateRepository, ISagaStateCommitter>();
        var wrapper = Substitute.For<ISagaStateRepository>();

        Assert.Throws<InvalidOperationException>(
            () => Resolve(committing, services => services.Decorate<ISagaStateRepository>((_, _) => wrapper)));
    }

    [Fact]
    public async Task the_decorated_service_stays_owned_by_the_container_and_keyed_services_are_left_alone()
    {
        var keyed = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection()
            .AddSingleton<IDisposableService, DisposableService>()
            .AddKeyedSingleton("other", keyed)
            .AddSingleton(Substitute.For<IOutboxRepository>())
            .Decorate<IDisposableService>((_, inner) => new Wrapper(inner));
        services.Decorate<IOutboxRepository>((_, inner) => inner);

        DisposableService original;
        await using (var provider = services.BuildServiceProvider())
        {
            var wrapper = Assert.IsType<Wrapper>(provider.GetRequiredService<IDisposableService>());
            original = Assert.IsType<DisposableService>(wrapper.Inner);
            Assert.Same(wrapper, provider.GetRequiredService<IDisposableService>());
            Assert.Same(keyed, provider.GetRequiredKeyedService<IOutboxRepository>("other"));
        }

        Assert.True(original.IsDisposed);
    }

    private static TService Resolve<TService>(TService original, Action<IServiceCollection> decorate)
        where TService : class
    {
        var services = new ServiceCollection().AddTransient(_ => original);
        decorate(services);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<TService>();
    }

    private sealed class CountingInterceptor : SagaStateRepositoryInterceptor
    {
        public int Commits { get; private set; }

        public override ValueTask CommitAsync(ISagaStateCommitter repository, ISagaInstance state, CancellationToken cancellationToken)
        {
            Commits++;
            return base.CommitAsync(repository, state, cancellationToken);
        }
    }

    public interface IDisposableService;

    private sealed class DisposableService : IDisposableService, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class Wrapper(IDisposableService inner) : IDisposableService
    {
        public IDisposableService Inner { get; } = inner;
    }
}
