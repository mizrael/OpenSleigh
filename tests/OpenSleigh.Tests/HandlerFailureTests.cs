using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace OpenSleigh.Tests;

/// <summary>
/// a handler that fails, on a host with the in-memory transport and persistence: the whole pipeline, no fakes.
/// </summary>
public class HandlerFailureTests
{
    public record PlaceOrder : IMessage;
    public record ShipOrder(string CorrelationId, bool CompensationFails = false) : IMessage, IHasCorrelationId;
    public record OrderShipped : IMessage;
    public record OrderCancelled : IMessage;

    public class OrderState
    {
        public List<string> Steps { get; set; } = new();
    }

    public class Journal
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public TaskCompletionSource Placed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Audited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// shipping an order always fails. Without <see cref="CompensatingOrderSaga"/>, nothing compensates it.
    /// </summary>
    public class OrderSaga : Saga<OrderState>, IStartedBy<PlaceOrder>, IHandleMessage<ShipOrder>, IHandleMessage<OrderCancelled>
    {
        protected readonly Journal Journal;

        public OrderSaga(Journal journal, ISagaInstance<OrderState> context) : base(context)
        {
            Journal = journal;
        }

        public ValueTask HandleAsync(IMessageContext<PlaceOrder> messageContext, CancellationToken cancellationToken = default)
        {
            Context.State.Steps.Add("placed");
            Journal.Placed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<ShipOrder> messageContext, CancellationToken cancellationToken = default)
        {
            Journal.Entries.Enqueue("order: shipping");
            Context.State.Steps.Add("shipped");
            Context.MarkAsCompleted();
            Publish(new OrderShipped());
            throw new InvalidOperationException("out of stock");
        }

        public ValueTask HandleAsync(IMessageContext<OrderCancelled> messageContext, CancellationToken cancellationToken = default)
        {
            Journal.Entries.Enqueue("order: cancelled, after " + string.Join(",", Context.State.Steps));
            Journal.Cancelled.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    public class CompensatingOrderSaga : OrderSaga, ICompensateMessage<ShipOrder>
    {
        public CompensatingOrderSaga(Journal journal, ISagaInstance<OrderState> context) : base(journal, context)
        {
        }

        public ValueTask CompensateAsync(IMessageContext<ShipOrder> messageContext, MessageFailure failure, CancellationToken cancellationToken = default)
        {
            Journal.Entries.Enqueue($"order: compensating '{failure.Exception.Message}' after {string.Join(",", Context.State.Steps)}");
            Context.State.Steps.Add("cancelled");
            Publish(new OrderCancelled());

            if (messageContext.Message.CompensationFails)
                throw new TimeoutException("the warehouse did not answer");

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// handles the message the order saga fails on, and does not fail.
    /// </summary>
    public class AuditSaga : Saga, IStartedBy<ShipOrder>, IHandleMessage<OrderShipped>
    {
        private readonly Journal _journal;

        public AuditSaga(Journal journal, ISagaInstance context) : base(context)
        {
            _journal = journal;
        }

        public ValueTask HandleAsync(IMessageContext<ShipOrder> messageContext, CancellationToken cancellationToken = default)
        {
            _journal.Entries.Enqueue("audit: shipping");
            _journal.Audited.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<OrderShipped> messageContext, CancellationToken cancellationToken = default)
        {
            _journal.Entries.Enqueue("order: shipped");
            return ValueTask.CompletedTask;
        }
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    // time for what must not happen: a message published by a failed handler, or a second attempt
    private static readonly TimeSpan TimeForWhatMustNotHappen = TimeSpan.FromMilliseconds(500);

    private sealed class Host<TOrderSaga> : IAsyncDisposable where TOrderSaga : OrderSaga
    {
        private readonly ServiceProvider _services;
        private readonly IHostedService[] _hostedServices;

        public Journal Journal { get; } = new();

        private Host()
        {
            _services = new ServiceCollection()
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddSingleton(Journal)
                .AddOpenSleigh(cfg => cfg
                    .UseInMemoryTransport()
                    .UseInMemoryPersistence()
                    .AddSaga<TOrderSaga, OrderState>()
                    .AddSaga<AuditSaga>())
                .BuildServiceProvider();
            _hostedServices = _services.GetServices<IHostedService>().ToArray();
        }

        public static async Task<Host<TOrderSaga>> StartAsync()
        {
            var host = new Host<TOrderSaga>();
            foreach (var hostedService in host._hostedServices)
                await hostedService.StartAsync(CancellationToken.None);
            return host;
        }

        public InMemoryDeadLetters DeadLetters => _services.GetRequiredService<InMemoryDeadLetters>();

        public async Task PublishAsync<TM>(TM message) where TM : IMessage
        {
            await using var scope = _services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);
        }

        /// <summary>
        /// places an order and returns its correlation id, once the saga has stored it.
        /// </summary>
        public async Task<string> PlaceOrderAsync()
        {
            await PublishAsync(new PlaceOrder());
            await Journal.Placed.Task.WaitAsync(Timeout);

            var sagas = _services.GetRequiredService<ISagaStateQuery>();
            var deadline = DateTimeOffset.UtcNow + Timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                var orders = await sagas.GetAllAsync(new SagaQueryFilter { SagaType = typeof(TOrderSaga).FullName });
                if (orders.Items is [{ IsLocked: false, ProcessedMessages.Count: 1 } order])
                    return order.CorrelationId;
                await Task.Delay(20);
            }

            throw new TimeoutException("the order was not stored.");
        }

        public async Task<SagaInstanceInfo> ReadOrderAsync()
        {
            var sagas = _services.GetRequiredService<ISagaStateQuery>();
            var orders = await sagas.GetAllAsync(new SagaQueryFilter { SagaType = typeof(TOrderSaga).FullName });
            return Assert.Single(orders.Items);
        }

        public async Task<IReadOnlyList<SagaMessageFailure>> WaitForFailuresAsync(int count)
        {
            var failures = _services.GetRequiredService<ISagaFailureQuery>();
            var deadline = DateTimeOffset.UtcNow + Timeout;
            while (true)
            {
                var page = await failures.GetAllAsync();
                if (page.Items.Count >= count || DateTimeOffset.UtcNow > deadline)
                    return page.Items;
                await Task.Delay(20);
            }
        }

        public async Task<InMemoryDeadLetter[]> WaitForDeadLettersAsync(int count)
        {
            var deadline = DateTimeOffset.UtcNow + Timeout;
            while (DeadLetters.Messages.Count < count && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(20);
            return DeadLetters.Messages.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var hostedService in _hostedServices)
                await hostedService.StopAsync(CancellationToken.None);
            await _services.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_failed_handler_without_compensation_should_dead_letter_the_message_once_and_leave_the_saga_as_it_was()
    {
        await using var host = await Host<OrderSaga>.StartAsync();
        var correlationId = await host.PlaceOrderAsync();

        await host.PublishAsync(new ShipOrder(correlationId));
        await host.Journal.Audited.Task.WaitAsync(Timeout);
        var deadLetters = await host.WaitForDeadLettersAsync(1);
        var failures = await host.WaitForFailuresAsync(1);
        await Task.Delay(TimeForWhatMustNotHappen);

        var deadLetter = Assert.Single(deadLetters);
        Assert.IsType<ShipOrder>(deadLetter.Message.Message);
        var error = Assert.IsType<SagaException>(deadLetter.Error);
        Assert.Equal("out of stock", error.InnerException!.Message);
        Assert.Single(host.DeadLetters.Messages);

        var entries = host.Journal.Entries.ToArray();
        Assert.Single(entries, "order: shipping");
        Assert.Single(entries, "audit: shipping");
        Assert.DoesNotContain("order: shipped", entries);

        var order = await host.ReadOrderAsync();
        Assert.Equal(["placed"], Assert.IsType<OrderState>(order.StateData).Steps);
        Assert.False(order.IsCompleted);
        Assert.False(order.IsLocked);
        Assert.Single(order.ProcessedMessages);

        var failure = Assert.Single(failures);
        Assert.Equal(FailureDecision.DeadLettered, failure.Decision);
        Assert.Equal(order.InstanceId, failure.InstanceId);
        Assert.Equal(correlationId, failure.CorrelationId);
        Assert.Equal(typeof(ShipOrder).FullName, failure.MessageType);
        Assert.Equal("out of stock", failure.ExceptionMessage);
        Assert.Equal(1, failure.Attempt);
    }

    [Fact]
    public async Task A_failed_handler_with_compensation_should_compensate_on_the_state_before_the_failure_and_not_dead_letter()
    {
        await using var host = await Host<CompensatingOrderSaga>.StartAsync();
        var correlationId = await host.PlaceOrderAsync();

        await host.PublishAsync(new ShipOrder(correlationId));
        await host.Journal.Cancelled.Task.WaitAsync(Timeout);
        await host.Journal.Audited.Task.WaitAsync(Timeout);
        var failures = await host.WaitForFailuresAsync(1);
        await Task.Delay(TimeForWhatMustNotHappen);

        var entries = host.Journal.Entries.ToArray();
        Assert.Single(entries, "order: shipping");
        Assert.Single(entries, "order: compensating 'out of stock' after placed");
        // what the compensation published was delivered, and found what the compensation stored
        Assert.Single(entries, "order: cancelled, after placed,cancelled");
        Assert.DoesNotContain("order: shipped", entries);
        Assert.Empty(host.DeadLetters.Messages);

        var order = await host.ReadOrderAsync();
        Assert.Equal(["placed", "cancelled"], Assert.IsType<OrderState>(order.StateData).Steps);
        Assert.False(order.IsCompleted);
        // the order, the failed message and the cancellation
        Assert.Equal(3, order.ProcessedMessages.Count);

        var failure = Assert.Single(failures);
        Assert.Equal(FailureDecision.Compensated, failure.Decision);
        Assert.Equal("out of stock", failure.ExceptionMessage);
    }

    [Fact]
    public async Task A_failed_compensation_should_dead_letter_the_message_and_leave_the_saga_as_it_was()
    {
        await using var host = await Host<CompensatingOrderSaga>.StartAsync();
        var correlationId = await host.PlaceOrderAsync();

        await host.PublishAsync(new ShipOrder(correlationId, CompensationFails: true));
        await host.Journal.Audited.Task.WaitAsync(Timeout);
        var deadLetters = await host.WaitForDeadLettersAsync(1);
        var failures = await host.WaitForFailuresAsync(1);
        await Task.Delay(TimeForWhatMustNotHappen);

        Assert.IsType<ShipOrder>(Assert.Single(deadLetters).Message.Message);
        Assert.Single(host.DeadLetters.Messages);

        var entries = host.Journal.Entries.ToArray();
        Assert.Single(entries, "order: shipping");
        Assert.Single(entries, "order: compensating 'out of stock' after placed");
        Assert.DoesNotContain(entries, entry => entry.StartsWith("order: cancelled"));

        var order = await host.ReadOrderAsync();
        Assert.Equal(["placed"], Assert.IsType<OrderState>(order.StateData).Steps);
        Assert.False(order.IsLocked);
        Assert.Single(order.ProcessedMessages);

        var failure = Assert.Single(failures);
        Assert.Equal(FailureDecision.CompensationFailed, failure.Decision);
        Assert.Equal("out of stock", failure.ExceptionMessage);
        Assert.Contains("the warehouse did not answer", failure.CompensationError);
    }
}
