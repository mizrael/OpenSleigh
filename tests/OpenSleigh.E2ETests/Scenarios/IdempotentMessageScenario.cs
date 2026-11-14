using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class IdempotentMessageScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int CopiesOfEachMessage = 4;
    private static readonly TimeSpan PublishRetryDelay = TimeSpan.FromMilliseconds(100);

    [Theory]
    [MatrixData]
    public async Task run_idempotent_message_scenario(string combination, int hostsCount)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

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

        var setup = new HostSetup(bus => bus.AddSaga<IdempotentSaga>())
        {
            ConfigureServices = services => services.AddSingleton(onMessage)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            var copies = Enumerable.Range(0, CopiesOfEachMessage).SelectMany(_ => messages);
            await Task.WhenAll(copies.Select(message => PublishAsync(run.AnyHost, message, cancellationToken)));

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
        ScenarioHost host,
        IdempotentMessage message,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await host.PublishAsync(message, cancellationToken);
            if (result is OutboxAppendResult.Success or OutboxAppendResult.Duplicate)
                return;

            await Task.Delay(PublishRetryDelay, cancellationToken);
        }
    }
}
