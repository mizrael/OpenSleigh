using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// what happens to a message the broker sends back because no queue took it, on substitutes of the channel and the outbox.
/// </summary>
public class ReturnedMessageHandlerTests
{
    private const string NoRoute = "NO_ROUTE";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);
    private static readonly QueueReferences References = new("orders", "orders.api", "orders", "orders.dead", "orders.dead.api");

    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly ILogger<ReturnedMessageHandler> _logger = Substitute.For<ILogger<ReturnedMessageHandler>>();
    private readonly MessageEnvelope _message = MessageEnvelope.Create(new FakeSagaStarter(), CreateSystemInfo());

    public ReturnedMessageHandlerTests()
    {
        _outbox.AppendAsync(default!, default).ReturnsForAnyArgs(OutboxAppendResult.Success);
    }

    [Fact]
    public async Task A_returned_message_should_go_back_to_the_outbox_to_be_published_again_later()
    {
        var appended = new List<MessageEnvelope>();
        _outbox.AppendAsync(Arg.Do<IEnumerable<MessageEnvelope>>(messages => appended.AddRange(messages)), Arg.Any<CancellationToken>())
               .Returns(OutboxAppendResult.Success);
        var sut = CreateSut(retryPeriod: TimeSpan.FromMinutes(5));
        var before = DateTimeOffset.UtcNow;

        await sut.HandleAsync(_channel, ReturnedFrom(References.ExchangeName));

        var putBack = Assert.Single(appended);
        Assert.Equal(_message.MessageId, putBack.MessageId);
        Assert.Equal(_message.CorrelationId, putBack.CorrelationId);
        Assert.IsType<FakeSagaStarter>(putBack.Message);
        Assert.InRange(putBack.DeliverAt!.Value, before + RetryDelay, DateTimeOffset.UtcNow + RetryDelay);
        Assert.Equal(LogLevel.Warning, Assert.Single(LoggedLevels()));
        Assert.Empty(_channel.ReceivedCalls());
    }

    [Fact]
    public async Task A_returned_message_should_wait_for_the_relay_to_remove_it_from_the_outbox()
    {
        _outbox.AppendAsync(default!, default)
               .ReturnsForAnyArgs(OutboxAppendResult.Duplicate, OutboxAppendResult.Duplicate, OutboxAppendResult.Success);
        var sut = CreateSut(retryPeriod: TimeSpan.FromMinutes(5));

        await sut.HandleAsync(_channel, ReturnedFrom(References.ExchangeName));

        await _outbox.ReceivedWithAnyArgs(3).AppendAsync(default!, default);
        Assert.DoesNotContain(LogLevel.Error, LoggedLevels());
    }

    [Fact]
    public async Task A_message_that_is_still_returned_when_the_period_is_over_should_go_to_its_dead_letter_exchange()
    {
        var sut = CreateSut(retryPeriod: TimeSpan.FromMilliseconds(50));

        await sut.HandleAsync(_channel, ReturnedFrom(References.ExchangeName));
        await Task.Delay(100);
        await sut.HandleAsync(_channel, ReturnedFrom(References.ExchangeName));

        // put back once, and not again
        await _outbox.ReceivedWithAnyArgs(1).AppendAsync(default!, default);
        await AssertDeadLetteredAsync();
        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Error }, LoggedLevels());
    }

    [Fact]
    public async Task A_returned_message_should_go_straight_to_its_dead_letter_exchange_when_there_is_no_period()
    {
        var sut = CreateSut(retryPeriod: TimeSpan.Zero);

        await sut.HandleAsync(_channel, ReturnedFrom(References.ExchangeName));

        await _outbox.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
        await AssertDeadLetteredAsync();
    }

    [Fact]
    public async Task A_dead_letter_that_no_queue_takes_either_should_be_dropped_and_reported()
    {
        var sut = CreateSut(retryPeriod: TimeSpan.FromMinutes(5));

        await sut.HandleAsync(_channel, ReturnedFrom(References.DeadLetterExchangeName));

        await _outbox.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
        Assert.Empty(_channel.ReceivedCalls());
        var entry = Assert.Single(_logger.ReceivedCalls());
        Assert.Equal(LogLevel.Error, entry.GetArguments().OfType<LogLevel>().Single());
        var text = entry.GetArguments()[2]!.ToString();
        Assert.Contains(_message.MessageId, text);
        Assert.Contains(typeof(FakeSagaStarter).FullName!, text);
        Assert.Contains(References.DeadLetterExchangeName, text);
    }

    [Fact]
    public async Task A_returned_message_that_cannot_be_put_back_should_be_reported_and_not_thrown()
    {
        _outbox.AppendAsync(default!, default).ReturnsForAnyArgs<ValueTask<OutboxAppendResult>>(
            _ => ValueTask.FromException<OutboxAppendResult>(new InvalidOperationException("the database is gone")));
        var sut = CreateSut(retryPeriod: TimeSpan.FromMinutes(5));

        await sut.HandleAsync(_channel, ReturnedFrom(References.ExchangeName));

        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Error }, LoggedLevels());
    }

    // the exchange is declared and no queue is, and the message is published as it was returned
    private async Task AssertDeadLetteredAsync()
    {
        await _channel.Received(1).ExchangeDeclareAsync(
            References.DeadLetterExchangeName, ExchangeType.Topic, Arg.Any<bool>(), Arg.Any<bool>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _channel.DidNotReceiveWithAnyArgs().QueueDeclareAsync(default!, default, default, default, default, default, default, default);
        await _channel.Received(1).BasicPublishAsync(
            References.DeadLetterExchangeName, References.DeadLetterQueue, true,
            Arg.Is<BasicProperties>(properties => properties.MessageId == _message.MessageId),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    private LogLevel[] LoggedLevels()
        => _logger.ReceivedCalls().Select(call => call.GetArguments().OfType<LogLevel>().Single()).ToArray();

    private ReturnedMessageHandler CreateSut(TimeSpan retryPeriod)
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeSagaStarter));

        var queueReferences = Substitute.For<IQueueReferenceFactory>();
        queueReferences.Create(Arg.Any<MessageEnvelope>()).Returns(References);

        var services = new ServiceCollection().AddSingleton(_outbox).BuildServiceProvider();

        return new ReturnedMessageHandler(
            services.GetRequiredService<IServiceScopeFactory>(),
            new RabbitMessageParser(typeResolver, new JsonSerializer()),
            queueReferences,
            new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: RetryDelay) { ReturnedMessageRetryPeriod = retryPeriod },
            _logger);
    }

    // the message as the publisher sends it, with its text headers as the broker hands them back: in bytes
    private BasicReturnEventArgs ReturnedFrom(string exchangeName)
    {
        var properties = new BasicProperties
        {
            MessageId = _message.MessageId,
            CorrelationId = _message.CorrelationId,
            Headers = new Dictionary<string, object?>
            {
                [nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes(_message.MessageType.FullName!),
                [nameof(MessageEnvelope.SenderId)] = Encoding.UTF8.GetBytes(_message.SenderId),
                [nameof(MessageEnvelope.CreatedAt)] = Encoding.UTF8.GetBytes(_message.CreatedAt.ToString()),
            }
        };

        return new BasicReturnEventArgs(
            312, NoRoute, exchangeName, exchangeName, properties, new JsonSerializer().Serialize(_message.Message), CancellationToken.None);
    }

    private static ISystemInfo CreateSystemInfo()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.Id.Returns(Guid.NewGuid().ToString());
        return systemInfo;
    }
}
