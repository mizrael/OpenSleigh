using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.ComponentModel;
using Bogus;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class ParallelMultiStartSagaScenario : E2ETestsBase
{
    private const int StartMessagesCount = 2;

    private static readonly Faker Faker = new();

    protected ParallelMultiStartSagaScenario(ITestOutputHelper console) : base(console) { }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_multiple_start_messages_scenario(int hostsCount)
    {
        var correlationId = Faker.Random.Guid().ToString();

        var start = new StartMultiStartSaga(Faker.Random.Int(), correlationId);
        var alsoStart = new AlsoStartMultiStartSaga(Faker.Lorem.Slug(5), correlationId);

        IMessage first = Faker.Random.Bool() ? start : alsoStart;
        IMessage last = ReferenceEquals(first, start) ? alsoStart : start;

        var handled = new Expectation(StartMessagesCount);
        ConcurrentBag<string> instanceIds = [];

        Action<IMessageContext<StartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onStart = (ctx, instance) =>
        {
            handled.Require(!string.IsNullOrWhiteSpace(ctx.MessageId), "the message has no id");
            handled.Require(!string.IsNullOrWhiteSpace(ctx.SenderId), "the message has no sender id");
            instanceIds.Add(instance.InstanceId);
            handled.Signal();
        };

        Action<IMessageContext<AlsoStartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onAlsoStart = (_, instance) =>
        {
            instanceIds.Add(instance.InstanceId);
            handled.Signal();
        };

        await RunScenarioAsync(
            hostsCount,
            (_, services) =>
            {
                services.AddSingleton(onStart);
                services.AddSingleton(onAlsoStart);
            },
            async (services, cancellationToken) =>
            {
                var bus = services.GetRequiredService<IMessageBus>();

                // the second message is on its way while the first is being handled
                await bus.PublishAsync(first, cancellationToken);
                await bus.PublishAsync(last, cancellationToken);

                await handled.Reached.WaitAsync(cancellationToken);
            });

        Assert.Empty(handled.Problems);
        Assert.Equal(StartMessagesCount, handled.Count);
        Assert.Single(instanceIds.Distinct());
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<MultiStartSaga, MultiStartSagaState>();
    }
}
