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
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly IOutboxReturnRepository _outbox = Substitute.For<IOutboxReturnRepository>();
    private readonly IChannel _deadLetterChannel = Substitute.For<IChannel>();
    private readonly IBusConnection _connection = Substitute.For<IBusConnection>();
    private readonly List<(LogLevel Level, string Text)> _log = [];
    private readonly List<MessageEnvelope> _putBack = [];
    private readonly Clock _clock = new() { Now = Now };

    private IOutboxReturnRepository Returns => _outbox;

    public ReturnedMessageHandlerTests()
    {
        _deadLetterChannel.IsOpen.Returns(true);
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(_deadLetterChannel);

        Returns.PutBackAsync(Arg.Do<IReadOnlyCollection<MessageEnvelope>>(_putBack.AddRange), Arg.Any<CancellationToken>())
               .Returns(ValueTask.CompletedTask);
    }

    [Fact]
    public async Task A_returned_message_should_go_back_to_the_outbox_to_be_published_again_after_the_retry_delay()
    {
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));

        // the worker is behind: the period counts from the return, the delay from when the message is stored
        var storedAt = _clock.Now = Now.AddMinutes(10);
        await sut.StoppedAsync(CancellationToken.None);

        var putBack = Assert.Single(_putBack);
        Assert.Equal("message-1", putBack.MessageId);
        Assert.IsType<FakeSagaStarter>(putBack.Message);
        Assert.Equal(storedAt + RetryDelay, putBack.DeliverAt);
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
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1", firstReturnedAt));
        await sut.StoppedAsync(CancellationToken.None);

        var putBack = Assert.Single(_putBack);
        Assert.Equal(firstReturnedAt, putBack.FirstReturnedAt);
        Assert.Equal(Now + RetryDelay, putBack.DeliverAt);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public async Task A_time_of_first_return_in_the_future_should_not_extend_the_period(int periodInMinutes)
    {
        var sut = CreateSut(TimeSpan.FromMinutes(periodInMinutes));

        sut.Accept(Returned("message-1", firstReturnedAt: Now.AddYears(1)));
        await sut.StoppedAsync(CancellationToken.None);

        if (periodInMinutes == 0)
        {
            Assert.Empty(_putBack);
            await AssertDeadLetteredAsync("message-1", times: 1);
        }
        else
        {
            Assert.Equal(Now, Assert.Single(_putBack).FirstReturnedAt);
        }
    }

    [Fact]
    public async Task A_message_that_still_comes_back_when_its_period_is_over_should_go_to_its_dead_letter_exchange_and_declare_no_queue()
    {
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Empty(_putBack);
        await _deadLetterChannel.Received(1).ExchangeDeclareAsync(
            DeadLetterExchange, ExchangeType.Topic, Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object?>>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _deadLetterChannel.DidNotReceiveWithAnyArgs().QueueDeclareAsync(default!, default, default, default, default, default, default, default);
        await AssertDeadLetteredAsync("message-1", times: 1);

        var error = Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text;
        Assert.Contains("message-1", error);
        Assert.Contains(DeadLetterExchange, error);
    }

    [Fact]
    public async Task A_message_should_be_given_to_its_dead_letter_exchange_again_on_another_channel_when_the_first_one_fails()
    {
        var secondChannel = Substitute.For<IChannel>();
        secondChannel.IsOpen.Returns(true);
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(_deadLetterChannel, secondChannel);
        _deadLetterChannel
            .BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default)
            .ReturnsForAnyArgs(_ => ValueTask.FromException(new InvalidOperationException("the channel is closed")));
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));
        await sut.StoppedAsync(CancellationToken.None);

        await secondChannel.Received(1).BasicPublishAsync(
            DeadLetterExchange, DeadLetterQueue, true, Arg.Is<BasicProperties>(properties => properties.MessageId == "message-1"),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        _deadLetterChannel.Received(1).Dispose();
        Assert.Empty(_putBack);
        Assert.Single(_log, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Messages_that_cannot_be_given_to_their_dead_letter_exchange_should_be_dropped_and_reported_and_never_put_back()
    {
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>())
                   .Returns(_ => Task.FromException<IChannel>(new InvalidOperationException("the broker is away")));
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();

        // the worker is busy with the first one: the next two are handled together
        sut.Accept(Returned("message-0"));
        await OutboxIsAskedAsync();
        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));
        sut.Accept(Returned("message-2", firstReturnedAt: Now - Period));
        release.SetResult();
        await sut.StoppedAsync(CancellationToken.None);

        var lost = _log.Where(entry => entry.Level == LogLevel.Error).Select(entry => entry.Text).ToArray();
        Assert.Equal(2, lost.Length);
        Assert.All(new[] { "message-1", "message-2" },
            messageId => Assert.Contains(lost, text => text.Contains(messageId) && text.Contains(Exchange) && text.Contains("the broker is away")));
        Assert.DoesNotContain(_log, entry => entry.Text.Contains("was given to"));

        // one attempt and one more for the first of them, none for the other
        await _connection.Received(2).CreateChannelAsync(Arg.Any<CancellationToken>());
        await Returns.Received(1).PutBackAsync(Arg.Any<IReadOnlyCollection<MessageEnvelope>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_should_be_put_back_with_a_period_of_zero_whatever_becomes_of_the_dead_letter()
    {
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>())
                   .Returns(_ => Task.FromException<IChannel>(new InvalidOperationException("the broker is away")));
        var sut = CreateSut(TimeSpan.Zero);

        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Contains("message-1", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
        await Returns.DidNotReceiveWithAnyArgs().PutBackAsync(default!, default);
    }

    [Fact]
    public async Task A_time_of_first_return_before_the_message_existed_should_not_shorten_the_period()
    {
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1", firstReturnedAt: Now.AddDays(-1)));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(Now, Assert.Single(_putBack).FirstReturnedAt);
    }

    [Fact]
    public async Task A_message_no_queue_takes_from_its_dead_letter_exchange_should_be_reported_as_dropped()
    {
        var sut = CreateSut(TimeSpan.Zero);
        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

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
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Single(_putBack);
    }

    [Fact]
    public async Task A_message_returned_twice_should_be_handled_once_as_it_first_came_back()
    {
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();

        // the worker is busy with the first one: the next two are handled together
        sut.Accept(Returned("message-0"));
        await OutboxIsAskedAsync();
        sut.Accept(Returned("message-1", firstReturnedAt: Now.AddMinutes(-1)));
        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));

        // a time that cannot be believed is set right before the times are compared: it is not the earliest
        sut.Accept(Returned("message-2", firstReturnedAt: Now.AddDays(-1)));
        sut.Accept(Returned("message-2", firstReturnedAt: Now.AddMinutes(-2)));
        sut.Accept(Returned("message-2", firstReturnedAt: Now.AddYears(1)));
        release.SetResult();
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(["message-0", "message-2"], _putBack.Select(message => message.MessageId));
        Assert.Equal(Now.AddMinutes(-2), _putBack[1].FirstReturnedAt);
        Assert.Equal(Now + RetryDelay, _putBack[1].DeliverAt);
        await AssertDeadLetteredAsync("message-1", times: 1);
    }

    [Fact]
    public async Task Messages_within_their_period_should_be_put_back_before_the_others_are_dead_lettered()
    {
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();
        var putBackWhenDeadLettering = -1;
        _deadLetterChannel
            .WhenForAnyArgs(channel => channel.BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default))
            .Do(_ => putBackWhenDeadLettering = _putBack.Count);

        sut.Accept(Returned("message-0"));
        await OutboxIsAskedAsync();
        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));
        sut.Accept(Returned("message-2"));
        release.SetResult();
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(["message-0", "message-2"], _putBack.Select(message => message.MessageId));
        Assert.Equal(2, putBackWhenDeadLettering);
    }

    [Fact]
    public async Task A_message_that_cannot_be_read_should_be_reported_and_not_stop_the_others()
    {
        var sut = CreateSut(Period);

        var unreadable = Properties("message-0", firstReturnedAt: null);
        unreadable.Headers![nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes("Not.A.Type");
        sut.Accept(new BasicReturnEventArgs(312, "NO_ROUTE", Exchange, Exchange, unreadable, Body(), CancellationToken.None));
        sut.Accept(Returned("message-1"));
        sut.Accept(Returned("message-2"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(["message-1", "message-2"], _putBack.Select(message => message.MessageId).Order());
        Assert.Contains("message-0", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
    }

    [Fact]
    public async Task A_message_should_be_put_back_again_when_the_outbox_fails_once()
    {
        var failures = 1;
        Returns.PutBackAsync(Arg.Any<IReadOnlyCollection<MessageEnvelope>>(), Arg.Any<CancellationToken>())
               .Returns(_ => failures-- > 0
                   ? throw new InvalidOperationException("the outbox is away")
                   : ValueTask.CompletedTask);
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        // asked twice: the first time failed
        Assert.Equal(["message-1", "message-1"], _putBack.Select(message => message.MessageId));
        Assert.DoesNotContain(_log, entry => entry.Level == LogLevel.Error);
        Assert.Contains(_log, entry => entry.Level == LogLevel.Warning && entry.Text.Contains("is back in the outbox"));
    }

    [Fact]
    public async Task Messages_should_be_put_back_for_as_long_as_it_takes_and_reported_as_lost_only_when_the_host_stops_waiting()
    {
        var attempts = 0;
        Returns.PutBackAsync(Arg.Any<IReadOnlyCollection<MessageEnvelope>>(), Arg.Any<CancellationToken>())
               .Returns(_ =>
               {
                   Interlocked.Increment(ref attempts);
                   return ValueTask.FromException(new TimeoutException("a relay holds the message"));
               });
        var sut = CreateSut(Period);
        using var hostGivesUp = new CancellationTokenSource();

        sut.Accept(Returned("message-1"));
        while (Volatile.Read(ref attempts) < 10)
            await Task.Delay(10);

        // the worker is still writing warnings
        lock (_log)
            Assert.DoesNotContain(_log, entry => entry.Level == LogLevel.Error);

        hostGivesUp.Cancel();
        await sut.StoppedAsync(hostGivesUp.Token);

        Assert.Contains("message-1", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
        Assert.DoesNotContain(_log, entry => entry.Text.Contains("is back in the outbox"));
    }

    [Fact]
    public async Task A_message_should_be_reported_as_lost_when_the_outbox_cannot_take_it_back()
    {
        var sut = CreateSut(Period, outboxTakesMessagesBack: false);

        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Contains("message-1", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
    }

    [Fact]
    public async Task Messages_should_still_be_taken_once_the_relay_was_told_to_stop_and_until_every_service_has_stopped()
    {
        var sut = CreateSut(Period);

        await sut.StoppingAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);
        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal("message-1", Assert.Single(_putBack).MessageId);

        sut.Accept(Returned("message-2"));

        Assert.Contains("message-2", Assert.Single(_log, entry => entry.Level == LogLevel.Error).Text);
        Assert.Single(_putBack);
    }

    [Fact]
    public async Task Every_message_that_is_left_should_be_reported_as_lost_when_the_host_does_not_wait_for_them()
    {
        var sut = CreateSut(Period);
        HoldTheOutbox();
        using var hostGivesUp = new CancellationTokenSource();

        for (var index = 0; index < 150; index++)
            sut.Accept(Returned($"message-{index:000}"));

        hostGivesUp.Cancel();
        await sut.StoppedAsync(hostGivesUp.Token);

        var lost = _log.Where(entry => entry.Level == LogLevel.Error).Select(entry => entry.Text).ToArray();
        Assert.Equal(150, lost.Length);
        Assert.All(Enumerable.Range(0, 150), index => Assert.Contains(lost, text => text.Contains($"message-{index:000}")));

        await sut.DisposeAsync();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task Messages_should_be_refused_and_reported_when_too_many_are_waiting()
    {
        const int capacity = 10_000;
        const int batchInFlight = 100;
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();

        for (var index = 0; index < capacity + batchInFlight + 50; index++)
            sut.Accept(Returned($"message-{index}"));

        var refused = _log.Count(entry => entry.Level == LogLevel.Error);
        Assert.InRange(refused, 50, 50 + batchInFlight);

        release.SetResult();
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(capacity + batchInFlight + 50 - refused, _putBack.Count);
    }

    [Fact]
    public async Task Messages_should_be_refused_and_reported_when_the_ones_that_are_waiting_are_too_large()
    {
        // a message that can be read, followed by blanks
        var large = new byte[48 * 1024 * 1024];
        Array.Fill(large, (byte)' ');
        SerializedMessage.CopyTo(large, 0);
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();

        // the ones the worker has taken and not stored yet count as well
        for (var index = 0; index < 4; index++)
            sut.Accept(new BasicReturnEventArgs(312, "NO_ROUTE", Exchange, Exchange, Properties($"message-{index}", null), large, CancellationToken.None));

        Assert.Contains("message-3", Assert.Single(_log, entry => entry.Level == LogLevel.Error && entry.Text.Contains("too many")).Text);

        release.SetResult();
        await sut.StoppedAsync(CancellationToken.None);
    }

    // the outbox does not answer until it is told to, or until whoever asks gives up
    private TaskCompletionSource HoldTheOutbox()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Returns.PutBackAsync(Arg.Any<IReadOnlyCollection<MessageEnvelope>>(), Arg.Any<CancellationToken>())
               .Returns(call => new ValueTask(HeldAsync(call.Arg<CancellationToken>())));
        return release;

        // what it is asked to store is recorded as it is asked
        async Task HeldAsync(CancellationToken cancellationToken)
        {
            _outboxAsked.Release();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private readonly SemaphoreSlim _outboxAsked = new(0);

    private async Task OutboxIsAskedAsync()
        => Assert.True(await _outboxAsked.WaitAsync(TimeSpan.FromSeconds(20)), "the outbox was not asked to take a message back.");

    private ValueTask AssertDeadLetteredAsync(string messageId, int times)
        => _deadLetterChannel.Received(times).BasicPublishAsync(
            DeadLetterExchange, DeadLetterQueue, true, Arg.Is<BasicProperties>(properties => properties.MessageId == messageId),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());

    private ReturnedMessageHandler CreateSut(TimeSpan retryPeriod, bool outboxTakesMessagesBack = true)
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeSagaStarter));
        var serializer = new JsonSerializer();

        var registrations = new ServiceCollection();
        if (outboxTakesMessagesBack)
            registrations.AddSingleton(_outbox);
        var services = registrations.BuildServiceProvider();

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

        return new ReturnedMessageHandler(
            services.GetRequiredService<IServiceScopeFactory>(),
            new RabbitMessageParser(typeResolver, serializer),
            new QueueReferenceFactory(_ => references),
            _connection,
            configuration,
            logger,
            _clock);
    }

    private static BasicReturnEventArgs Returned(string messageId, DateTimeOffset? firstReturnedAt = null)
        => new(312, "NO_ROUTE", Exchange, Exchange, Properties(messageId, firstReturnedAt), Body(), CancellationToken.None);

    private static readonly byte[] SerializedMessage = new JsonSerializer().Serialize(new FakeSagaStarter());

    private static ReadOnlyMemory<byte> Body() => SerializedMessage;

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

    /// <summary>the time the test says it is. What waits for a delay does not wait.</summary>
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => base.CreateTimer(callback, state, TimeSpan.Zero, period);
    }
}
