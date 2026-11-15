using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// what happens to a message the broker returned, with the parser, type resolver and serializer of the library:
/// the outbox and the broker are the real ones in the integration and end-to-end tests.
/// </summary>
public class ReturnedMessageHandlerTests
{
    private const string Exchange = "the-exchange";
    private const string DeadLetterExchange = "the-exchange.dead";
    private const string DeadLetterQueue = "the-exchange.dead.api";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository, IOutboxReturnRepository>();
    private readonly IChannel _deadLetterChannel = Substitute.For<IChannel>();
    private readonly IBusConnection _connection = Substitute.For<IBusConnection>();
    private readonly List<(LogLevel Level, string Text)> _log = [];
    private readonly List<MessageEnvelope> _putBack = [];

    public ReturnedMessageHandlerTests()
    {
        _deadLetterChannel.IsOpen.Returns(true);
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(_deadLetterChannel);

        ((IOutboxReturnRepository)_outbox)
            .PutBackAsync(Arg.Do<IReadOnlyCollection<MessageEnvelope>>(_putBack.AddRange), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
    }

    [Fact]
    public async Task A_returned_message_should_go_back_to_the_outbox_to_be_published_again_after_the_retry_delay()
    {
        var sut = CreateSut(TimeSpan.FromMinutes(5));

        sut.Accept(Returned("message-1"));
        await sut.StopAsync(CancellationToken.None);

        var putBack = Assert.Single(_putBack);
        Assert.Equal("message-1", putBack.MessageId);
        Assert.IsType<FakeSagaStarter>(putBack.Message);
        Assert.Equal(Now + RetryDelay, putBack.DeliverAt);
        Assert.Equal(Now, putBack.FirstReturnedAt);

        var warning = Assert.Single(_log, entry => entry.Level == LogLevel.Warning).Text;
        Assert.Contains("message-1", warning);
        Assert.Contains(typeof(FakeSagaStarter).FullName!, warning);
        Assert.Contains(Exchange, warning);
        Assert.Contains("NO_ROUTE", warning);
        await _connection.DidNotReceiveWithAnyArgs().CreateChannelAsync(default);
    }

    [Fact]
    public async Task A_message_that_comes_back_again_within_its_period_should_keep_the_time_it_first_came_back()
    {
        var firstReturnedAt = Now.AddMinutes(-4);
        var sut = CreateSut(TimeSpan.FromMinutes(5));

        sut.Accept(Returned("message-1", firstReturnedAt));
        await sut.StopAsync(CancellationToken.None);

        var putBack = Assert.Single(_putBack);
        Assert.Equal(firstReturnedAt, putBack.FirstReturnedAt);
        Assert.Equal(Now + RetryDelay, putBack.DeliverAt);
    }

    [Fact]
    public async Task A_message_that_still_comes_back_when_its_period_is_over_should_go_to_its_dead_letter_exchange_and_declare_no_queue()
    {
        var sut = CreateSut(TimeSpan.FromMinutes(5));

        sut.Accept(Returned("message-1", firstReturnedAt: Now.AddMinutes(-5)));
        await sut.StopAsync(CancellationToken.None);

        Assert.Empty(_putBack);
        await _deadLetterChannel.Received(1).ExchangeDeclareAsync(
            DeadLetterExchange, ExchangeType.Topic, Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object?>>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _deadLetterChannel.DidNotReceiveWithAnyArgs().QueueDeclareAsync(default!, default, default, default, default, default, default, default);
        await _deadLetterChannel.Received(1).BasicPublishAsync(
            DeadLetterExchange, DeadLetterQueue, true, Arg.Is<BasicProperties>(properties => properties.MessageId == "message-1"),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());

        var error = Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text;
        Assert.Contains("message-1", error);
        Assert.Contains(DeadLetterExchange, error);
    }

    [Fact]
    public async Task A_message_no_queue_takes_from_its_dead_letter_exchange_should_be_reported_as_dropped()
    {
        var sut = CreateSut(TimeSpan.Zero);
        sut.Accept(Returned("message-1"));
        await sut.StopAsync(CancellationToken.None);

        var deadLetter = new BasicReturnEventArgs(
            312, "NO_ROUTE", DeadLetterExchange, DeadLetterQueue, Properties("message-1", firstReturnedAt: null), ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        _deadLetterChannel.BasicReturnAsync += Raise.Event<AsyncEventHandler<BasicReturnEventArgs>>(_deadLetterChannel, deadLetter);

        var dropped = Assert.Single(_log, entry => entry.Level == LogLevel.Error && entry.Text.Contains("was dropped")).Text;
        Assert.Contains("message-1", dropped);
        Assert.Contains(typeof(FakeSagaStarter).FullName!, dropped);
        Assert.Contains(DeadLetterExchange, dropped);
        Assert.Empty(_putBack);
    }

    [Fact]
    public async Task Any_period_should_be_accepted()
    {
        var sut = CreateSut(TimeSpan.MaxValue);

        sut.Accept(Returned("message-1", firstReturnedAt: DateTimeOffset.MinValue.AddDays(1)));
        await sut.StopAsync(CancellationToken.None);

        Assert.Single(_putBack);
    }

    [Fact]
    public async Task Messages_returned_together_should_be_put_back_together_and_one_that_cannot_be_read_should_not_stop_the_others()
    {
        var sut = CreateSut(TimeSpan.FromMinutes(5));

        var unreadable = Properties("message-0", firstReturnedAt: null);
        unreadable.Headers![nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes("Not.A.Type");
        sut.Accept(new BasicReturnEventArgs(312, "NO_ROUTE", Exchange, Exchange, unreadable, Body(), CancellationToken.None));
        sut.Accept(Returned("message-1"));
        sut.Accept(Returned("message-2"));
        await sut.StopAsync(CancellationToken.None);

        Assert.Equal(["message-1", "message-2"], _putBack.Select(message => message.MessageId).Order());
        Assert.Contains("message-0", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
    }

    [Fact]
    public async Task A_message_should_be_put_back_again_when_the_outbox_fails_once()
    {
        var failures = 1;
        ((IOutboxReturnRepository)_outbox)
            .PutBackAsync(Arg.Any<IReadOnlyCollection<MessageEnvelope>>(), Arg.Any<CancellationToken>())
            .Returns(_ => failures-- > 0
                ? throw new InvalidOperationException("the outbox is away")
                : ValueTask.CompletedTask);
        var sut = CreateSut(TimeSpan.FromMinutes(5));

        sut.Accept(Returned("message-1"));
        await sut.StopAsync(CancellationToken.None);

        // asked twice: the first time failed
        Assert.Equal(["message-1", "message-1"], _putBack.Select(message => message.MessageId));
        Assert.DoesNotContain(_log, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_message_should_be_reported_as_lost_when_the_outbox_cannot_take_it_back()
    {
        var sut = CreateSut(TimeSpan.FromMinutes(5), Substitute.For<IOutboxRepository>());

        sut.Accept(Returned("message-1"));
        await sut.StopAsync(CancellationToken.None);

        Assert.Contains("message-1", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
    }

    [Fact]
    public async Task A_message_returned_once_the_host_has_stopped_should_be_reported_as_lost()
    {
        var sut = CreateSut(TimeSpan.FromMinutes(5));
        await sut.StopAsync(CancellationToken.None);

        sut.Accept(Returned("message-1"));

        Assert.Empty(_putBack);
        Assert.Contains("message-1", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
    }

    private ReturnedMessageHandler CreateSut(TimeSpan retryPeriod, IOutboxRepository? outbox = null)
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeSagaStarter));
        var serializer = new JsonSerializer();

        var services = new ServiceCollection().AddSingleton(outbox ?? _outbox).BuildServiceProvider();

        var references = new QueueReferences(Exchange, "the-exchange.api", Exchange, DeadLetterExchange, DeadLetterQueue);
        var configuration = new RabbitConfiguration("host", "user", "password", retryDelay: RetryDelay) { ReturnedMessageRetryPeriod = retryPeriod };

        var logger = Substitute.For<ILogger<ReturnedMessageHandler>>();
        logger.IsEnabled(default).ReturnsForAnyArgs(true);
        logger.WhenForAnyArgs(l => l.Log<object>(default, default, default!, default, default!))
              .Do(call =>
              {
                  lock (_log)
                      _log.Add((call.Arg<LogLevel>(), call.Args()[2]!.ToString()!));
              });

        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(Now);

        return new ReturnedMessageHandler(
            services.GetRequiredService<IServiceScopeFactory>(),
            new RabbitMessageParser(typeResolver, serializer),
            new QueueReferenceFactory(_ => references),
            _connection,
            configuration,
            logger,
            clock);
    }

    private static BasicReturnEventArgs Returned(string messageId, DateTimeOffset? firstReturnedAt = null)
        => new(312, "NO_ROUTE", Exchange, Exchange, Properties(messageId, firstReturnedAt), Body(), CancellationToken.None);

    private static ReadOnlyMemory<byte> Body() => new JsonSerializer().Serialize(new FakeSagaStarter());

    // as the broker hands them back: the text of a header is bytes
    private static BasicProperties Properties(string messageId, DateTimeOffset? firstReturnedAt)
    {
        var headers = new Dictionary<string, object?>
        {
            [nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes(typeof(FakeSagaStarter).FullName!),
            [nameof(MessageEnvelope.SenderId)] = Encoding.UTF8.GetBytes("sender"),
            [nameof(MessageEnvelope.CreatedAt)] = Encoding.UTF8.GetBytes(Now.AddMinutes(-10).ToString()),
        };
        if (firstReturnedAt is not null)
            headers[nameof(MessageEnvelope.FirstReturnedAt)] = Encoding.UTF8.GetBytes(firstReturnedAt.Value.ToString("O", CultureInfo.InvariantCulture));

        return new BasicProperties { MessageId = messageId, CorrelationId = "correlation", Headers = headers };
    }
}
