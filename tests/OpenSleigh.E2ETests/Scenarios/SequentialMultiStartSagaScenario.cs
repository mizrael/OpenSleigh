using System.Collections.Concurrent;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class SequentialMultiStartSagaScenario(ITestOutputHelper console) : ScenarioBase(console)
{
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

        var firstHandled = new Expectation(1);
        var bothHandled = new Expectation(2);
        ConcurrentBag<string> instanceIds = [];

        void OnHandled(ISagaInstance instance)
        {
            instanceIds.Add(instance.InstanceId);
            firstHandled.Signal();
            bothHandled.Signal();
        }

        Action<IMessageContext<StartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onStart = (_, instance) => OnHandled(instance);
        Action<IMessageContext<AlsoStartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onAlsoStart = (_, instance) => OnHandled(instance);

        var setup = new HostSetup(bus => bus.AddSaga<MultiStartSaga, MultiStartSagaState>())
        {
            ConfigureServices = services => services.AddSingleton(onStart).AddSingleton(onAlsoStart)
        };

        object? storedState = null;

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            if (startFirst)
                await run.AnyHost.PublishAsync(start, cancellationToken);
            else
                await run.AnyHost.PublishAsync(alsoStart, cancellationToken);

            await firstHandled.Reached.WaitAsync(cancellationToken);

            // the second message is published once the saga is stored and released by the first
            await run.WaitForSagaAsync<MultiStartSaga>(correlationId,
                stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked,
                cancellationToken);

            if (startFirst)
                await run.AnyHost.PublishAsync(alsoStart, cancellationToken);
            else
                await run.AnyHost.PublishAsync(start, cancellationToken);

            await bothHandled.Reached.WaitAsync(cancellationToken);

            storedState = (await run.WaitForSagaAsync<MultiStartSaga>(correlationId,
                stored => stored.ProcessedMessages.Count == 2 && !stored.IsLocked,
                cancellationToken)).StateData;
        });

        Assert.Equal(2, bothHandled.Count);
        Assert.Single(instanceIds.Distinct());
        Assert.Equal(new MultiStartSagaState { Foo = foo, Bar = bar }, storedState);
    }
}
