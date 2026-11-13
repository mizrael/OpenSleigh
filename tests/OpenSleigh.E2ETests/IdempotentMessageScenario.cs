using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.ComponentModel;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class IdempotentMessageScenario : E2ETestsBase
{
    private const int CopiesOfEachMessage = 4;

    protected IdempotentMessageScenario(ITestOutputHelper console) : base(console) { }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_idempotent_message_scenario(int hostsCount)
    {
#if NET9_0_OR_GREATER
        var requestId = Guid.CreateVersion7().ToString("N");
        var correlationId = Guid.CreateVersion7().ToString("N");
#else
        var requestId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");
#endif
        var messages = new[]
        {
            new IdempotentMessage(requestId, correlationId, 0),
            new IdempotentMessage(requestId, correlationId, 1)
        };

        var received = messages.Select(_ => new Expectation(1)).ToArray();

        Action<IMessageContext<IdempotentMessage>, ISagaInstance> onMessage = (ctx, saga) =>
        {
            var expectation = received[ctx.Message.Foo];
            expectation.Require(correlationId == ctx.CorrelationId, "the context has another correlation id");
            expectation.Require(correlationId == ctx.Message.CorrelationId, "the message has another correlation id");
            expectation.Require(requestId == ctx.Message.RequestId, "the message has another request id");
            expectation.Signal();
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async (services, cancellationToken) =>
            {
                var bus = services.GetRequiredService<IMessageBus>();

                var copies = Enumerable.Range(0, CopiesOfEachMessage).SelectMany(_ => messages);
                await Task.WhenAll(copies.Select(message => PublishAsync(bus, message, cancellationToken)));

                await Task.WhenAll(received.Select(expectation => expectation.Reached)).WaitAsync(cancellationToken);
            });

        Assert.All(received, expectation =>
        {
            Assert.Empty(expectation.Problems);
            Assert.Equal(1, expectation.Count);
        });
    }

    // a copy either gets into the outbox or is refused as a duplicate of one that is there:
    // anything else is tried again
    private static async Task PublishAsync(
        IMessageBus bus,
        IdempotentMessage message,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await bus.PublishAsync(message, cancellationToken);
            if (result is OutboxAppendResult.Success or OutboxAppendResult.Duplicate)
                return;

            await Task.Delay(100, cancellationToken);
        }
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<IdempotentSaga>();
    }
}
