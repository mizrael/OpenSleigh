using System.Collections.Concurrent;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class ParallelMultiStartSagaScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int StartMessagesCount = 2;

    private static readonly Faker Faker = new();

    [Theory]
    [MatrixData]
    public async Task run_multiple_start_messages_scenario(string combination, int hostsCount)
    {
        var correlationId = Faker.Random.Guid().ToString();

        var foo = Faker.Random.Int();
        var bar = Faker.Lorem.Slug(5);

        var start = new StartMultiStartSaga(foo, correlationId);
        var alsoStart = new AlsoStartMultiStartSaga(bar, correlationId);
        var startFirst = Faker.Random.Bool();

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

        var setup = new HostSetup(bus => bus.AddSaga<MultiStartSaga, MultiStartSagaState>())
        {
            ConfigureServices = services => services.AddSingleton(onStart).AddSingleton(onAlsoStart)
        };

        object? storedState = null;

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            // the second message is on its way while the first is being handled
            if (startFirst)
            {
                await run.AnyHost.PublishAsync(start, cancellationToken);
                await run.AnyHost.PublishAsync(alsoStart, cancellationToken);
            }
            else
            {
                await run.AnyHost.PublishAsync(alsoStart, cancellationToken);
                await run.AnyHost.PublishAsync(start, cancellationToken);
            }

            await handled.Reached.WaitAsync(cancellationToken);

            // what each message wrote is in the stored state, whichever was handled first
            storedState = (await run.WaitForSagaAsync<MultiStartSaga>(correlationId,
                stored => stored.ProcessedMessages.Count == StartMessagesCount && !stored.IsLocked,
                cancellationToken)).StateData;
        });

        Assert.Empty(handled.Problems);
        Assert.Equal(StartMessagesCount, handled.Count);
        Assert.Single(instanceIds.Distinct());
        Assert.Equal(new MultiStartSagaState { Foo = foo, Bar = bar }, storedState);
    }
}
