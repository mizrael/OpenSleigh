using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Bogus;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class ParallelMultiStartSagaScenario : E2ETestsBase
{
    private static readonly Faker Faker = new();

    protected ParallelMultiStartSagaScenario(ITestOutputHelper console) : base(console) { }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_multiple_start_messages_scenario(int hostsCount)
    {
        var correlationId = Faker.Random.Guid().ToString();
        var foo = Faker.Random.Int();
        var bar = Faker.Lorem.Slug(5);

        var start = new StartMultiStartSaga(foo, correlationId, Guid.NewGuid().ToString("N"));
        var alsoStart = new AlsoStartMultiStartSaga(bar, correlationId, Guid.NewGuid().ToString("N"));

        IMessage first = Faker.Random.Bool() ? start : alsoStart;
        IMessage last = ReferenceEquals(first, start) ? alsoStart : start;

        var receivedCount = 0;
        var timeout = 10 * hostsCount;
        if (Debugger.IsAttached)
        {
            timeout = 1000;
        }
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

        MultiStartSagaState state = null!;

        HashSet<string> instanceIds = [];

        Action<IMessageContext<StartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onStart = (ctx, inst) =>
        {
            Console.WriteLine($"Handled Start message {JsonSerializer.Serialize(ctx.Message, new JsonSerializerOptions { WriteIndented = true })}");
            ctx.MessageId.Should().NotBeNullOrWhiteSpace();
            ctx.SenderId.Should().NotBeNullOrWhiteSpace();

            instanceIds.Add(inst.InstanceId);
            receivedCount++;
            if (ReferenceEquals(last, start))
            {
                state = inst.State;
                // ReSharper disable once AccessToDisposedClosure
                tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            }
        };

        Action<IMessageContext<AlsoStartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onAlsoStart = (ctx, inst) =>
        {
            Console.WriteLine($"Handled AlsoStart message {JsonSerializer.Serialize(ctx.Message, new JsonSerializerOptions { WriteIndented = true })}");
            instanceIds.Add(inst.InstanceId);
            receivedCount++;
            if (ReferenceEquals(last, alsoStart))
            {
                state = inst.State;
                // ReSharper disable once AccessToDisposedClosure
                tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            }
        };

        await RunScenarioAsync(
            hostsCount,
            (_, services) =>
            {
                services.AddSingleton(onStart);
                services.AddSingleton(onAlsoStart);
            },
            async bus =>
            {
                Console.WriteLine($"Start message Id: {start.RequestId}");
                Console.WriteLine($"AlsoStart message Id: {alsoStart.RequestId}");

                await bus.PublishAsync(first, tokenSource.Token);
                await bus.PublishAsync(last, tokenSource.Token);
            },
            tokenSource
        );

        using var assertionScope = new AssertionScope();
        receivedCount.Should().Be(2);
        instanceIds.Should().HaveCount(1);

        // These work correctly for in-memory scenarios because the object is always the same
        // Not having access to the host's container, I'm not sure if there's an appropriate way to get at the
        // saga state in the physical-persistence scenarios
        // state.Foo.Should().Be(foo);
        // state.Bar.Should().Be(bar);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<MultiStartSaga, MultiStartSagaState>();
    }
}
