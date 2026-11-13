using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenSleigh.Tests;

/// <summary>
/// a saga that publishes its own timeout, on the in-memory transport and persistence, relayed by the real outbox service.
/// </summary>
public class DelayedDeliveryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan OutboxInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task A_timeout_should_be_handled_once_and_not_before_it_is_due_when_the_reply_never_arrives()
    {
        await using var provider = BuildProvider();
        var probe = provider.GetRequiredService<TimeoutProbe>();
        await StartHostedServicesAsync(provider);

        var request = new StartRequest(Guid.NewGuid().ToString(), ReplyArrives: false);
        await PublishAsync(provider, request);

        await WaitUntilAsync(() => probe.TimeoutsHandled(request.CorrelationId) > 0);
        await Task.Delay(4 * OutboxInterval);

        Assert.Equal(1, probe.TimeoutsHandled(request.CorrelationId));
        Assert.True(probe.TimeToTimeout(request.CorrelationId) >= Timeout);

        await StopHostedServicesAsync(provider);
    }

    [Fact]
    public async Task A_timeout_should_not_be_handled_and_should_leave_the_outbox_when_the_saga_completed_before_it_was_due()
    {
        await using var provider = BuildProvider();
        var probe = provider.GetRequiredService<TimeoutProbe>();
        await StartHostedServicesAsync(provider);

        var request = new StartRequest(Guid.NewGuid().ToString(), ReplyArrives: true);
        await PublishAsync(provider, request);

        await WaitUntilAsync(() => probe.IsCompleted(request.CorrelationId));
        await Task.Delay(Timeout + 8 * OutboxInterval);

        Assert.Equal(0, probe.TimeoutsHandled(request.CorrelationId));
        Assert.Empty(await provider.GetRequiredService<IOutboxRepository>().ReadPendingAsync());

        // nothing is waiting either: a message that is still delayed would show up once it is due
        await Task.Delay(Timeout);
        Assert.Empty(await provider.GetRequiredService<IOutboxRepository>().ReadPendingAsync());

        await StopHostedServicesAsync(provider);
    }

    private static ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeoutProbe>()
            .AddOpenSleigh(cfg => cfg
                .UseInMemoryPersistence()
                .UseInMemoryTransport()
                .WithOutboxProcessorOptions(new OutboxProcessorOptions(OutboxInterval))
                .AddSaga<RequestSaga>())
            .BuildServiceProvider();

    private static async Task StartHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
    }

    private static async Task StopHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
            await service.StopAsync(CancellationToken.None);
    }

    private static async Task PublishAsync(IServiceProvider provider, StartRequest request)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(request);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < WaitLimit, "the expected message was not handled in time");
            await Task.Delay(10);
        }
    }

    public record StartRequest(string CorrelationId, bool ReplyArrives) : IMessage, IHasCorrelationId;

    public record Reply : IMessage;

    public record ReplyTimedOut : IMessage;

    public sealed class TimeoutProbe
    {
        private readonly ConcurrentDictionary<string, long> _started = new();
        private readonly ConcurrentDictionary<string, int> _timeouts = new();
        private readonly ConcurrentDictionary<string, TimeSpan> _timeToTimeout = new();
        private readonly ConcurrentDictionary<string, bool> _completed = new();

        public void Started(string correlationId) => _started[correlationId] = Stopwatch.GetTimestamp();

        public void TimedOut(string correlationId)
        {
            _timeToTimeout.TryAdd(correlationId, Stopwatch.GetElapsedTime(_started[correlationId]));
            _timeouts.AddOrUpdate(correlationId, 1, (_, count) => count + 1);
        }

        public void Completed(string correlationId) => _completed[correlationId] = true;

        public int TimeoutsHandled(string correlationId) => _timeouts.GetValueOrDefault(correlationId);
        public TimeSpan TimeToTimeout(string correlationId) => _timeToTimeout[correlationId];
        public bool IsCompleted(string correlationId) => _completed.ContainsKey(correlationId);
    }

    public sealed class RequestSaga : Saga,
        IStartedBy<StartRequest>,
        IHandleMessage<Reply>,
        IHandleMessage<ReplyTimedOut>
    {
        private readonly TimeoutProbe _probe;

        public RequestSaga(ISagaInstance context, TimeoutProbe probe) : base(context)
        {
            _probe = probe;
        }

        public ValueTask HandleAsync(IMessageContext<StartRequest> messageContext, CancellationToken cancellationToken = default)
        {
            // the clock starts before the timeout is published: the time measured is never shorter than the delay
            _probe.Started(messageContext.CorrelationId);

            if (messageContext.Message.ReplyArrives)
                Publish(new Reply());
            Publish(new ReplyTimedOut(), Timeout);

            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<Reply> messageContext, CancellationToken cancellationToken = default)
        {
            Context.MarkAsCompleted();
            _probe.Completed(messageContext.CorrelationId);
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<ReplyTimedOut> messageContext, CancellationToken cancellationToken = default)
        {
            _probe.TimedOut(messageContext.CorrelationId);
            Context.MarkAsCompleted();
            return ValueTask.CompletedTask;
        }
    }
}
