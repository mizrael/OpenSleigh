using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh.Tests;

/// <summary>
/// a handler that fails, on a host with the in-memory transport and persistence: the whole pipeline, no fakes.
/// </summary>
public class HandlerFailureTests
{
    public record PlaceOrder : IMessage;
    public record OrderShipped : IMessage;
    public record OrderCancelled : IMessage;

    public class Journal
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Audited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public class OrderSaga : Saga, IStartedBy<PlaceOrder>, IHandleMessage<OrderShipped>, IHandleMessage<OrderCancelled>
    {
        private readonly Journal _journal;

        public OrderSaga(Journal journal, ISagaInstance context) : base(context)
        {
            _journal = journal;
        }

        public ValueTask HandleAsync(IMessageContext<PlaceOrder> messageContext, CancellationToken cancellationToken = default)
        {
            _journal.Entries.Enqueue("order: placing");
            Publish(new OrderShipped());
            throw new InvalidOperationException("out of stock");
        }

        public ValueTask RollbackAsync(IMessageContext<PlaceOrder> messageContext, CancellationToken cancellationToken = default)
        {
            _journal.Entries.Enqueue("order: rolling back");
            Publish(new OrderCancelled());
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<OrderShipped> messageContext, CancellationToken cancellationToken = default)
        {
            _journal.Entries.Enqueue("order: shipped");
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<OrderCancelled> messageContext, CancellationToken cancellationToken = default)
        {
            _journal.Entries.Enqueue("order: cancelled");
            _journal.Cancelled.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    public class AuditSaga : Saga, IStartedBy<PlaceOrder>
    {
        private readonly Journal _journal;

        public AuditSaga(Journal journal, ISagaInstance context) : base(context)
        {
            _journal = journal;
        }

        public ValueTask HandleAsync(IMessageContext<PlaceOrder> messageContext, CancellationToken cancellationToken = default)
        {
            _journal.Entries.Enqueue("audit: placing");
            _journal.Audited.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task A_failed_handler_should_deliver_what_its_rollback_published_and_dead_letter_the_message_once()
    {
        var journal = new Journal();
        await using var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(journal)
            .AddOpenSleigh(cfg => cfg
                .UseInMemoryTransport()
                .UseInMemoryPersistence()
                .AddSaga<OrderSaga>()
                .AddSaga<AuditSaga>())
            .BuildServiceProvider();
        var hostedServices = services.GetServices<IHostedService>().ToArray();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        var timeout = TimeSpan.FromSeconds(20);
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new PlaceOrder());
        await journal.Cancelled.Task.WaitAsync(timeout);
        await journal.Audited.Task.WaitAsync(timeout);
        // time for what must not happen: the message the failed handler published, or a second attempt
        await Task.Delay(500);
        foreach (var hostedService in hostedServices)
            await hostedService.StopAsync(CancellationToken.None);

        var entries = journal.Entries.ToArray();
        Assert.Single(entries, "order: placing");
        Assert.Single(entries, "order: rolling back");
        Assert.Single(entries, "order: cancelled");
        Assert.Single(entries, "audit: placing");
        Assert.DoesNotContain("order: shipped", entries);

        var deadLetter = Assert.Single(services.GetRequiredService<InMemoryDeadLetters>().Messages);
        Assert.IsType<PlaceOrder>(deadLetter.Message.Message);
        var failure = Assert.IsType<SagaException>(deadLetter.Error);
        Assert.True(failure.RolledBack);
        Assert.Equal("out of stock", failure.InnerException!.Message);
    }
}
