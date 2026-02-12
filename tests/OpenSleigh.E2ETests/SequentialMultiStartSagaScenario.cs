using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.ComponentModel;
using Bogus;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class SequentialMultiStartSagaScenario : E2ETestsBase
{
    private static readonly Faker Faker = new();

    public SequentialMultiStartSagaScenario(ITestOutputHelper console) : base(console) { }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_multiple_start_messages_scenario(int hostsCount)
    {
        var correlationId = Faker.Random.Guid().ToString();
        var foo = Faker.Random.Int();
        var bar = Faker.Lorem.Slug(5);

        var start = new StartMultiStartSaga(foo, correlationId);
        var alsoStart = new AlsoStartMultiStartSaga(bar, correlationId);

        IMessage first = Faker.Random.Bool() ? start : alsoStart;
        IMessage last = ReferenceEquals(first, start) ? alsoStart : start;

        var receivedCount = 0;
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10) * hostsCount);

        MultiStartSagaState state = null!;
        var handled = false;

        Action<IMessageContext<StartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onStart = (_, inst) =>
        {
            receivedCount++;
            handled = true;
            if (ReferenceEquals(last, start))
            {
                state = inst.State;
                // ReSharper disable once AccessToDisposedClosure
                tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            }
        };

        Action<IMessageContext<AlsoStartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onAlsoStart = (_, inst) =>
        {
            receivedCount++;
            handled = true;
            if (ReferenceEquals(last, alsoStart))
            {
                state = inst.State;
                // ReSharper disable once AccessToDisposedClosure
                tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            }
        };

        await RunScenarioAsync(hostsCount,
            (_, services) =>
            {
                services.AddSingleton(onStart);
                services.AddSingleton(onAlsoStart);
            },
            async bus =>
            {
                await bus.PublishAsync(first, tokenSource.Token);

                while (!handled)
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                // wait one more second for unlocking
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                await bus.PublishAsync(last, tokenSource.Token);
            },
            tokenSource);

        Assert.Equal(2, receivedCount);

        // These work correctly for in-memory scenarios because the object is always the same
        // Not having access to the host's container, I'm not sure if there's an appropriate way to get at the
        // saga state in the physical-persistence scenarios
        // Assert.Equal(foo, state.Foo);
        // Assert.Equal(bar, state.Bar);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<MultiStartSaga, MultiStartSagaState>();
    }
}
