using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.ComponentModel;
using Bogus;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class SimpleMultiStartSagaScenario : E2ETestsBase
{
    private static readonly Faker Faker = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_multiple_start_messages_scenario(int hostsCount)
    {
        var correlationId = Faker.Random.Guid().ToString();
        var foo = Faker.Random.Int();
        var bar = Faker.Lorem.Slug(5);

        var start = new StartSimpleMultiStartSaga(foo, correlationId);
        var alsoStart = new AlsoStartSimpleMultiStartSaga(bar, correlationId);

        IMessage first = Faker.Random.Bool() ? start : alsoStart;
        IMessage last = ReferenceEquals(first, start) ? alsoStart : start;

        var receivedCount = 0;
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10) * hostsCount);

        SimpleMultiStartSagaState state = null!;
        var handled = false;

        Action<IMessageContext<StartSimpleMultiStartSaga>, SimpleMultiStartSagaState> onStart = (ctx, st) =>
        {
            ctx.MessageId.Should().NotBeNullOrWhiteSpace();
            ctx.SenderId.Should().NotBeNullOrWhiteSpace();

            state = st;
            receivedCount++;
            handled = true;
            if (ReferenceEquals(last, start))
                tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
        };

        Action<IMessageContext<AlsoStartSimpleMultiStartSaga>> onAlsoStart = ctx =>
        {
            ctx.MessageId.Should().NotBeNullOrWhiteSpace();
            ctx.SenderId.Should().NotBeNullOrWhiteSpace();

            receivedCount++;
            handled = true;
            if (ReferenceEquals(last, alsoStart))
                tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) =>
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

        receivedCount.Should().Be(2);

        // These work correctly for in-memory scenarios because the object is always the same
        // Not having access to the host's container, I'm not sure if there's an appropriate way to get at the
        // saga state in the physical-persistence scenarios
        // state.Foo.Should().Be(foo);
        // state.Bar.Should().Be(bar);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<SimpleMultiStartSaga, SimpleMultiStartSagaState>();
    }
}
