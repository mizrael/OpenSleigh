using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;
using System.ComponentModel;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class IdempotentMessageScenario : E2ETestsBase
{
    private int _maxHostsCount = 10;

    protected IdempotentMessageScenario(int maxHostsCount = 10)
    {
        _maxHostsCount = maxHostsCount;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_idempotent_message_scenario(int hostsCount)
    {
        if (hostsCount > _maxHostsCount)
            return;

#if NET9_0_OR_GREATER
        var requestId = Guid.CreateVersion7().ToString("N");
        var correlationId = Guid.CreateVersion7().ToString("N");
#else
        var requestId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");
#endif
        var message1 = new IdempotentMessage(requestId, correlationId, 0);
        var message2 = new IdempotentMessage(requestId, correlationId, 1);

        var receivedCount = new[] { 0, 0 };
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10) * hostsCount);

        Action<IMessageContext<IdempotentMessage>, ISagaInstance> onMessage = (ctx, saga) =>
        {
            Assert.NotNull(ctx.Message);
            Assert.Equal(correlationId, ctx.CorrelationId);
            Assert.Equal(correlationId, ctx.Message.CorrelationId);
            Assert.Equal(requestId, ctx.Message.RequestId);

            var newCount = Interlocked.Increment(ref receivedCount[ctx.Message.Foo]);
            if (newCount > 1)
                throw new InvalidOperationException($"Message with Foo={ctx.Message.Foo} was received more than once.");
            
            if (Volatile.Read(ref receivedCount[0]) > 0 && Volatile.Read(ref receivedCount[1]) > 0)
                tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async bus =>
            {
                await Task.WhenAll([
                    PublishAsync(bus, message1, tokenSource.Token),
                    PublishAsync(bus, message2, tokenSource.Token),
                    PublishAsync(bus, message1, tokenSource.Token),
                    PublishAsync(bus, message2, tokenSource.Token),
                    PublishAsync(bus, message1, tokenSource.Token),
                    PublishAsync(bus, message2, tokenSource.Token),
                    PublishAsync(bus, message1, tokenSource.Token),
                    PublishAsync(bus, message2, tokenSource.Token),
                ]);
            },
            tokenSource);

        Assert.All(receivedCount, i => Assert.Equal(1, i));
    }

    private async Task PublishAsync(
        IMessageBus bus,
        IdempotentMessage message,
        CancellationToken cancellationToken)
    {
        var result = Outbox.OutboxAppendResult.Undefined;
        while (result != Outbox.OutboxAppendResult.Success &&
                result != Outbox.OutboxAppendResult.Duplicate)
        {
            try
            {
                result = await bus.PublishAsync(message, cancellationToken);
                if (result != Outbox.OutboxAppendResult.Success &&
                    result != Outbox.OutboxAppendResult.Duplicate)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<IdempotentSaga>();
    }
}