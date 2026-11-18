using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenSleigh.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// what the subscriber tells the broker about a message whose saga is locked, on a substitute of the channel.
/// </summary>
public class RabbitMessageSubscriberRetryTests
{
    private const ulong DeliveryTag = 42;
    private const string Acknowledged = "acknowledged";
    private const string PublishedToRetry = "published to the retry exchange";

    private static readonly QueueReferences References = new("invoices", "invoices.workers", "invoices", "invoices.dead", "invoices.dead.workers");

    [Fact]
    public async Task A_message_whose_saga_is_locked_should_be_in_the_retry_queue_before_it_is_acknowledged()
    {
        var channel = Substitute.For<IChannel>();
        var calls = RecordCallsOf(channel);
        var consumer = await StartSubscriberAsync(channel);

        await DeliverAsync(consumer);

        Assert.Equal([PublishedToRetry, Acknowledged], calls);
        await channel.Received(1).BasicPublishAsync(
            References.RetryExchangeName, References.RetryQueueName, true, Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_message_that_could_not_be_published_to_the_retry_exchange_of_a_closed_channel_should_be_left_to_the_broker()
    {
        var channel = Substitute.For<IChannel>();
        var calls = RecordCallsOf(channel);
        channel.BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default)
               .ThrowsAsyncForAnyArgs(ChannelClosed());
        var consumer = await StartSubscriberAsync(channel);

        // the failure goes to whoever dispatched the delivery, as that of any other acknowledgement does
        await Assert.ThrowsAsync<AlreadyClosedException>(() => DeliverAsync(consumer));

        // the broker still has it, and delivers it again now that the channel is closed
        Assert.DoesNotContain(Acknowledged, calls);
        await channel.DidNotReceiveWithAnyArgs().BasicRejectAsync(default, default, default);
    }

    [Fact]
    public async Task A_message_that_was_published_to_the_retry_exchange_and_could_not_be_acknowledged_should_not_be_rejected()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        channel.BasicAckAsync(default, default, default).ThrowsAsyncForAnyArgs(ChannelClosed());
        var calls = RecordCallsOf(channel);
        var consumer = await StartSubscriberAsync(channel);

        await Assert.ThrowsAsync<AlreadyClosedException>(() => DeliverAsync(consumer));

        // its copy is in the retry queue: dead-lettered as well, it would be handled and given up on
        Assert.Equal([PublishedToRetry, Acknowledged], calls);
        await channel.DidNotReceiveWithAnyArgs().BasicRejectAsync(default, default, default);
    }

    [Fact]
    public async Task A_message_that_could_not_be_published_to_the_retry_exchange_of_an_open_channel_should_be_dead_lettered()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        var calls = RecordCallsOf(channel);
        channel.BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default)
               .ThrowsAsyncForAnyArgs(new ArgumentOutOfRangeException("value", "a short string of 765 bytes"));
        var logger = new RecordingLogger();
        var consumer = await StartSubscriberAsync(channel, logger);

        await DeliverAsync(consumer);

        // the broker would never take it back from a channel that stays open
        Assert.DoesNotContain(Acknowledged, calls);
        await channel.Received(1).BasicRejectAsync(DeliveryTag, false, Arg.Any<CancellationToken>());
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Text.Contains(References.RetryExchangeName));
    }

    [Fact]
    public async Task A_channel_that_the_broker_closes_should_be_reported_as_an_error()
    {
        var channel = Substitute.For<IChannel>();
        var logger = new RecordingLogger();
        await StartSubscriberAsync(channel, logger);

        channel.ChannelShutdownAsync += Raise.Event<AsyncEventHandler<ShutdownEventArgs>>(
            channel, new ShutdownEventArgs(ShutdownInitiator.Application, Constants.ReplySuccess, "the host is stopping"));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);

        channel.ChannelShutdownAsync += Raise.Event<AsyncEventHandler<ShutdownEventArgs>>(
            channel, new ShutdownEventArgs(ShutdownInitiator.Peer, Constants.NotFound, "NOT_FOUND - no exchange 'invoices.retry'"));

        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("404", error.Text);
        Assert.Contains("NOT_FOUND - no exchange 'invoices.retry'", error.Text);
    }

    private static AlreadyClosedException ChannelClosed()
        => new(new ShutdownEventArgs(ShutdownInitiator.Peer, Constants.ConnectionForced, "CONNECTION_FORCED"));

    private static List<string> RecordCallsOf(IChannel channel)
    {
        var calls = new List<string>();
        channel.WhenForAnyArgs(c => c.BasicAckAsync(default, default, default))
               .Do(_ => calls.Add(Acknowledged));
        channel.WhenForAnyArgs(c => c.BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default))
               .Do(_ => calls.Add(PublishedToRetry));
        return calls;
    }

    private static async Task<IAsyncBasicConsumer> StartSubscriberAsync(IChannel channel, ILogger<RabbitMessageSubscriber>? logger = null)
    {
        IAsyncBasicConsumer? consumer = null;
        channel.WhenForAnyArgs(c => c.BasicConsumeAsync(default!, default, default!, default, default, default, default!, default))
               .Do(call => consumer = call.Arg<IAsyncBasicConsumer>());

        var channelFactory = Substitute.For<IChannelFactory>();
        channelFactory.GetConsumeChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var queueReferenceFactory = Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.Create(Arg.Any<Type>()).Returns(References);
        queueReferenceFactory.Create(Arg.Any<MessageEnvelope>()).Returns(References);

        var descriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        descriptorsResolver.GetRegisteredMessageTypes().Returns([typeof(FakeSagaStarter)]);

        var processor = Substitute.For<IMessageProcessor>();
        processor.ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
                 .ThrowsAsync(new LockException("the saga is locked"));

        var sut = new RabbitMessageSubscriber(
            channelFactory,
            queueReferenceFactory,
            new RabbitConfiguration("localhost", "guest", "guest"),
            new ServiceCollection().AddSingleton(processor).BuildServiceProvider(),
            new ParsedAlready(CreateEnvelope()),
            descriptorsResolver,
            logger ?? NullLogger<RabbitMessageSubscriber>.Instance);
        await sut.StartAsync();

        return consumer ?? throw new InvalidOperationException("the subscriber did not start a consumer.");
    }

    private static Task DeliverAsync(IAsyncBasicConsumer consumer)
        => consumer.HandleBasicDeliverAsync(
            consumerTag: "consumer",
            deliveryTag: DeliveryTag,
            redelivered: false,
            exchange: References.ExchangeName,
            routingKey: References.RoutingKey,
            properties: new BasicProperties(),
            body: ReadOnlyMemory<byte>.Empty);

    private static MessageEnvelope CreateEnvelope()
    {
        var saga = Substitute.For<ISagaInstance>();
        saga.CorrelationId.Returns(Guid.NewGuid().ToString());
        saga.TriggerMessageId.Returns(Guid.NewGuid().ToString());
        saga.InstanceId.Returns(Guid.NewGuid().ToString());
        return MessageEnvelope.Create(new FakeSagaStarter(), saga);
    }

    // the interface is not visible to the assembly substitutes are made in
    private sealed class ParsedAlready(MessageEnvelope envelope) : IRabbitMessageParser
    {
        public Task<MessageEnvelope> ParseMessageAsync(BasicDeliverEventArgs eventArgs) => Task.FromResult(envelope);

        public MessageEnvelope Parse(IReadOnlyBasicProperties properties, ReadOnlySpan<byte> body) => envelope;
    }

    // a logger of an internal type cannot be substituted either
    private sealed class RecordingLogger : ILogger<RabbitMessageSubscriber>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
