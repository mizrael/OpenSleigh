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
    [HostsCountData]
    public async Task run_multiple_start_messages_scenario(int hostsCount)
    {
        var correlationId = Faker.Random.Guid().ToString();

        var foo = Faker.Random.Int();
        var bar = Faker.Lorem.Slug(5);

        var start = new StartMultiStartSaga(foo, correlationId);
        var alsoStart = new AlsoStartMultiStartSaga(bar, correlationId);

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

        object? storedState = null;

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

                // what each message wrote is in the stored state, whichever was handled first
                storedState = (await WaitForSagaAsync<MultiStartSaga>(services, correlationId,
                    stored => stored.ProcessedMessages.Count == StartMessagesCount && !stored.IsLocked,
                    cancellationToken)).StateData;
            });

        Assert.Empty(handled.Problems);
        Assert.Equal(StartMessagesCount, handled.Count);
        Assert.Single(instanceIds.Distinct());
        Assert.Equal(new MultiStartSagaState { Foo = foo, Bar = bar }, storedState);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<MultiStartSaga, MultiStartSagaState>();
    }
}
