using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.InMemory.Messaging;
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

    // how long a test waits for something that must not happen. Nothing tells that the transport is idle:
    // it is several passes of the relay, and far longer than a message stays in the in-memory channel
    private static readonly TimeSpan SettleTime = 8 * OutboxInterval;

    [Fact]
    public async Task A_timeout_should_be_handled_once_and_not_before_it_is_due_when_the_reply_never_arrives()
    {
        await using var provider = BuildProvider();
        var probe = provider.GetRequiredService<TimeoutProbe>();
        await StartHostedServicesAsync(provider);

        var request = new StartRequest(Guid.NewGuid().ToString(), ReplyArrives: false);
        await PublishAsync(provider, request);

        await WaitUntilAsync(() => probe.TimeoutsHandled(request.CorrelationId) > 0, "the timeout was not handled");
        await WaitUntilOutboxIsEmptyAsync(provider);
        await Task.Delay(SettleTime);

        Assert.Equal(1, probe.TimeoutsHandled(request.CorrelationId));
        Assert.True(
            probe.TimeoutHandledAt(request.CorrelationId) >= probe.TimeoutNotDueBefore(request.CorrelationId),
            "the timeout was handled before it was due");
        Assert.Empty(provider.GetRequiredService<InMemoryDeadLetters>().Messages);

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

        await WaitUntilAsync(() => probe.IsCompleted(request.CorrelationId), "the saga did not complete");
        await WaitUntilAsync(
            () => DateTimeOffset.UtcNow > probe.TimeoutDueBy(request.CorrelationId),
            "the due time of the timeout did not pass");

        // a read after the due time returns the timeout until the relay has published and removed it
        await WaitUntilOutboxIsEmptyAsync(provider);
        await Task.Delay(SettleTime);

        Assert.Equal(0, probe.TimeoutsHandled(request.CorrelationId));
        Assert.Empty(await provider.GetRequiredService<IOutboxRepository>().ReadPendingAsync());

        // not handled is not failed: a timeout that comes too late is not a dead letter
        Assert.Empty(provider.GetRequiredService<InMemoryDeadLetters>().Messages);

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

    private static Task WaitUntilAsync(Func<bool> condition, string failure)
        => WaitUntilAsync(() => ValueTask.FromResult(condition()), failure);

    private static Task WaitUntilOutboxIsEmptyAsync(IServiceProvider provider)
    {
        var outbox = provider.GetRequiredService<IOutboxRepository>();
        return WaitUntilAsync(async () => !(await outbox.ReadPendingAsync()).Any(), "the outbox was not emptied");
    }

    private static async Task WaitUntilAsync(Func<ValueTask<bool>> condition, string failure)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(stopwatch.Elapsed < WaitLimit, $"{failure} in time");
            await Task.Delay(10);
        }
    }

    public record StartRequest(string CorrelationId, bool ReplyArrives) : IMessage, IHasCorrelationId;

    public record Reply : IMessage;

    public record ReplyTimedOut : IMessage;

    /// <summary>
    /// the times are read from the clock the due time of a message is computed from, and compared with.
    /// </summary>
    public sealed class TimeoutProbe
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _notDueBefore = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _dueBy = new();
        private readonly ConcurrentDictionary<string, int> _timeouts = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _handledAt = new();
        private readonly ConcurrentDictionary<string, bool> _completed = new();

        // the due time of the timeout is the time it was published at plus its delay: it lies between these two
        public void PublishingTimeout(string correlationId) => _notDueBefore[correlationId] = DateTimeOffset.UtcNow + Timeout;
        public void PublishedTimeout(string correlationId) => _dueBy[correlationId] = DateTimeOffset.UtcNow + Timeout;

        public void TimedOut(string correlationId)
        {
            _handledAt.TryAdd(correlationId, DateTimeOffset.UtcNow);
            _timeouts.AddOrUpdate(correlationId, 1, (_, count) => count + 1);
        }

        public void Completed(string correlationId) => _completed[correlationId] = true;

        public int TimeoutsHandled(string correlationId) => _timeouts.GetValueOrDefault(correlationId);
        public DateTimeOffset TimeoutNotDueBefore(string correlationId) => _notDueBefore[correlationId];
        public DateTimeOffset TimeoutDueBy(string correlationId) => _dueBy[correlationId];
        public DateTimeOffset TimeoutHandledAt(string correlationId) => _handledAt[correlationId];
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
            if (messageContext.Message.ReplyArrives)
                Publish(new Reply());

            _probe.PublishingTimeout(messageContext.CorrelationId);
            Publish(new ReplyTimedOut(), Timeout);
            _probe.PublishedTimeout(messageContext.CorrelationId);

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
