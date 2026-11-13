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

        var start = new StartMultiStartSaga(Faker.Random.Int(), correlationId);
        var alsoStart = new AlsoStartMultiStartSaga(Faker.Lorem.Slug(5), correlationId);

        IMessage first = Faker.Random.Bool() ? start : alsoStart;
        IMessage last = ReferenceEquals(first, start) ? alsoStart : start;

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

        await RunScenarioAsync(hostsCount,
            (_, services) =>
            {
                services.AddSingleton(onStart);
                services.AddSingleton(onAlsoStart);
            },
            async (services, cancellationToken) =>
            {
                var bus = services.GetRequiredService<IMessageBus>();

                await bus.PublishAsync(first, cancellationToken);
                await firstHandled.Reached.WaitAsync(cancellationToken);

                // the second message is published once the saga is stored and released by the first
                var saga = await WaitForSagaAsync<MultiStartSaga>(services, correlationId,
                    stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked,
                    cancellationToken);
                Console.WriteLine($"saga after its first message: {saga}, state: {saga.StateData}");

                await bus.PublishAsync(last, cancellationToken);
                await bothHandled.Reached.WaitAsync(cancellationToken);
            });

        Assert.Equal(2, bothHandled.Count);
        Assert.Single(instanceIds.Distinct());
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<MultiStartSaga, MultiStartSagaState>();
    }
}
