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
    private const string StoppedReason = "stopped before";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = Now.AddMinutes(-10);

    private readonly IOutboxReturnRepository _outbox = Substitute.For<IOutboxReturnRepository>();
    private readonly IChannel _deadLetterChannel = Substitute.For<IChannel>();
    private readonly IBusConnection _connection = Substitute.For<IBusConnection>();
    private readonly List<(LogLevel Level, string Text)> _log = [];
    private readonly List<MessageEnvelope> _putBack = [];
    private readonly Clock _clock = new() { Now = Now };

    public ReturnedMessageHandlerTests()
    {
        _deadLetterChannel.IsOpen.Returns(true);
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(_deadLetterChannel);

        // every message the outbox is asked to store, whatever it answers
        _outbox.PutBackAsync(Arg.Do<MessageEnvelope>(Record), Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
    }

    // the worker writes while the test reads
    private void Record(MessageEnvelope message)
    {
        lock (_putBack)
            _putBack.Add(message);
    }

    private int PutBackCount
    {
        get
        {
            lock (_putBack)
                return _putBack.Count;
        }
    }

    private string[] Errors
    {
        get
        {
            lock (_log)
                return _log.Where(entry => entry.Level == LogLevel.Error).Select(entry => entry.Text).ToArray();
        }
    }

    [Fact]
    public async Task A_returned_message_should_go_back_to_the_outbox_to_be_published_again_after_the_retry_delay()
    {
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        var putBack = Assert.Single(_putBack);
        Assert.Equal("message-1", putBack.MessageId);
        Assert.IsType<FakeSagaStarter>(putBack.Message);
        Assert.Equal(CreatedAt, putBack.CreatedAt);
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
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1", firstReturnedAt));
        await sut.StoppedAsync(CancellationToken.None);

        var putBack = Assert.Single(_putBack);
        Assert.Equal(firstReturnedAt, putBack.FirstReturnedAt);
        Assert.Equal(Now + RetryDelay, putBack.DeliverAt);
    }

    [Theory]
    [InlineData(365)]
    [InlineData(-1)]
    public async Task A_time_of_first_return_the_message_cannot_have_should_not_be_believed(int daysFromNow)
    {
        var sut = CreateSut(Period);

        // after it came back, or before it existed: its period would never end, or be over at once
        sut.Accept(Returned("message-1", firstReturnedAt: Now.AddDays(daysFromNow)));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(Now, Assert.Single(_putBack).FirstReturnedAt);
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
        await AssertDeadLetteredAsync(_deadLetterChannel, "message-1");

        var error = Assert.Single(Errors);
        Assert.Contains("message-1", error);
        Assert.Contains(DeadLetterExchange, error);
    }

    [Fact]
    public async Task Any_period_should_be_accepted()
    {
        var sut = CreateSut(TimeSpan.MaxValue);

        sut.Accept(Returned("message-1", firstReturnedAt: CreatedAt.AddMinutes(1)));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(CreatedAt.AddMinutes(1), Assert.Single(_putBack).FirstReturnedAt);
    }

    [Fact]
    public async Task A_message_should_be_given_to_its_dead_letter_exchange_again_on_another_channel_when_the_first_one_fails()
    {
        var secondChannel = Substitute.For<IChannel>();
        secondChannel.IsOpen.Returns(true);
        var firstChannel = FailingChannel();
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(firstChannel, secondChannel);
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));
        await sut.StoppedAsync(CancellationToken.None);

        await AssertDeadLetteredAsync(secondChannel, "message-1");
        Assert.Empty(_putBack);
        Assert.Contains("was given to", Assert.Single(Errors));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 4)]
    public async Task Messages_that_cannot_be_given_to_their_dead_letter_exchange_should_be_dropped_and_reported_and_never_put_back(
        bool connected, int expectedChannels)
    {
        // without a connection the second message is not tried: it would wait as long as the first to find the broker away
        _connection.IsConnected.Returns(connected);
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>())
                   .Returns(_ => connected ? Task.FromResult(FailingChannel()) : Task.FromException<IChannel>(new InvalidOperationException("it failed")));
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();

        // the worker is busy with the first one: the next two are handled in one pass
        sut.Accept(Returned("message-0"));
        await OutboxIsAskedAsync();
        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));
        sut.Accept(Returned("message-2", firstReturnedAt: Now - Period));
        release.SetResult();
        await sut.StoppedAsync(CancellationToken.None);

        var lost = Errors.ToArray();
        Assert.Equal(2, lost.Length);
        Assert.All(new[] { "message-1", "message-2" },
            messageId => Assert.Contains(lost, text => text.Contains(messageId) && text.Contains(Exchange) && text.Contains("it failed")));
        Assert.Equal("message-0", Assert.Single(_putBack).MessageId);
        await _connection.Received(expectedChannels).CreateChannelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_should_be_put_back_with_a_period_of_zero_whatever_becomes_of_the_dead_letter()
    {
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>())
                   .Returns(_ => Task.FromException<IChannel>(new InvalidOperationException("it failed")));
        var sut = CreateSut(TimeSpan.Zero);

        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Contains("message-1", Assert.Single(Errors));
        Assert.Empty(_putBack);
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

        var dropped = Assert.Single(Errors, text => text.Contains("was dropped"));
        Assert.Contains("message-1", dropped);
        Assert.Contains(typeof(FakeSagaStarter).FullName!, dropped);
        Assert.Contains(DeadLetterExchange, dropped);
        Assert.Empty(_putBack);
    }

    [Fact]
    public async Task Messages_within_their_period_should_be_put_back_before_the_others_are_dead_lettered()
    {
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();
        var putBackWhenDeadLettering = -1;
        _deadLetterChannel
            .WhenForAnyArgs(channel => channel.BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default))
            .Do(_ => putBackWhenDeadLettering = PutBackCount);

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

        sut.Accept(Unreadable("message-0"));
        sut.Accept(Returned("message-1"));
        sut.Accept(Returned("message-2"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(["message-1", "message-2"], _putBack.Select(message => message.MessageId));
        Assert.Contains("message-0", Assert.Single(Errors));
    }

    [Fact]
    public async Task A_message_that_cannot_be_stored_should_be_tried_again_on_its_own_after_the_others()
    {
        var failures = 1;
        var bothAreWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
               .Returns(_ => failures-- > 0 ? new ValueTask(FailAsync()) : ValueTask.CompletedTask);
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        sut.Accept(Returned("message-2"));
        bothAreWaiting.SetResult();

        await WaitUntilAsync(() => PutBackCount == 3);
        await sut.StoppedAsync(CancellationToken.None);

        async Task FailAsync()
        {
            await bothAreWaiting.Task;
            throw new InvalidOperationException("the outbox is away");
        }


        Assert.Equal(["message-1", "message-2", "message-1"], _putBack.Select(message => message.MessageId));
        Assert.Empty(Errors);
        Assert.Contains("message-1", Assert.Single(_log, entry => entry.Text.Contains("could not be put back")).Text);
        Assert.Equal(2, _log.Count(entry => entry.Text.Contains("is back in the outbox")));
    }

    [Fact]
    public async Task A_message_that_can_never_be_stored_should_go_to_its_dead_letter_exchange_once_its_period_is_over()
    {
        // the period runs out while the message waits to be tried again
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
               .Returns(_ =>
               {
                   _clock.Now = Now + Period;
                   return ValueTask.FromException(new InvalidOperationException("the message is too large"));
               });
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        await WaitUntilAsync(() => Errors.Any());
        await sut.StoppedAsync(CancellationToken.None);

        await AssertDeadLetteredAsync(_deadLetterChannel, "message-1");
        Assert.Single(_putBack);
        Assert.Contains("was given to", Assert.Single(Errors));
    }

    [Fact]
    public async Task A_message_that_cannot_be_stored_should_be_reported_as_lost_when_the_application_stops()
    {
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
               .Returns(_ => ValueTask.FromException(new InvalidOperationException("the outbox is away")));
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        await WaitUntilAsync(() => PutBackCount >= 3);
        await sut.StoppedAsync(CancellationToken.None);

        var lost = Assert.Single(Errors);
        Assert.Contains("message-1", lost);
        Assert.Contains("the outbox is away", lost);
        Assert.DoesNotContain(_log, entry => entry.Text.Contains("is back in the outbox"));
    }

    [Fact]
    public async Task A_message_should_be_reported_as_lost_when_the_outbox_cannot_take_it_back()
    {
        var sut = CreateSut(Period, outboxTakesMessagesBack: false);

        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Contains("message-1", Assert.Single(Errors));
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

        Assert.Contains("message-2", Assert.Single(Errors));
        Assert.Single(_putBack);
    }

    [Fact]
    public async Task Every_message_that_is_left_should_be_reported_as_lost_and_not_looked_at_when_the_host_does_not_wait_for_them()
    {
        const int waiting = 150;
        var sut = CreateSut(Period);
        HoldTheOutbox();
        using var hostGivesUp = new CancellationTokenSource();

        // the worker is busy with the first one. One of the others cannot be read, and one is past its period:
        // nobody finds out
        sut.Accept(Returned("message-first"));
        await OutboxIsAskedAsync();
        sut.Accept(Unreadable("message-000"));
        sut.Accept(Returned("message-001", firstReturnedAt: Now - Period));
        for (var index = 2; index < waiting; index++)
            sut.Accept(Returned($"message-{index:000}"));

        hostGivesUp.Cancel();
        await sut.StoppedAsync(hostGivesUp.Token);

        var lost = Errors.ToArray();
        Assert.Equal(waiting + 1, lost.Length);
        Assert.All(lost, text => Assert.Contains(StoppedReason, text));
        Assert.All(Enumerable.Range(0, waiting), index => Assert.Contains(lost, text => text.Contains($"message-{index:000}")));
        await _connection.DidNotReceiveWithAnyArgs().CreateChannelAsync(default);

        await sut.DisposeAsync();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task Messages_should_be_refused_and_reported_when_too_many_are_waiting()
    {
        const int capacity = 10_000;
        const int tooMany = 50;
        var sut = CreateSut(Period);
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
               .Returns(_ => Interlocked.Increment(ref attempts) == 1 ? new ValueTask(firstAttempt.Task) : ValueTask.CompletedTask);

        // the worker is busy with the first one, which has its place among the ones that wait
        sut.Accept(Returned("message-first"));
        await WaitUntilAsync(() => PutBackCount == 1);
        for (var index = 0; index < capacity - 1 + tooMany; index++)
            sut.Accept(Returned($"message-{index}"));

        Assert.Equal(tooMany, Errors.Count(text => text.Contains("too many")));

        // the first one cannot be stored: its place is still there when it waits again
        firstAttempt.SetException(new InvalidOperationException("the outbox is away"));
        await WaitUntilAsync(() => PutBackCount == capacity + 1);
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(tooMany, Errors.Length);
        Assert.Equal("message-first", _putBack[^1].MessageId);
    }

    [Fact]
    public async Task A_message_should_keep_its_room_until_it_is_given_to_its_dead_letter_exchange()
    {
        var large = LargeBody();
        var deadLettering = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _deadLetterChannel
            .BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default)
            .ReturnsForAnyArgs(_ =>
            {
                deadLettering.Release();
                return new ValueTask(release.Task);
            });
        var sut = CreateSut(TimeSpan.Zero);

        // the broker keeps the first one waiting: it is still here
        sut.Accept(Returned("message-0", body: large));
        Assert.True(await deadLettering.WaitAsync(TimeSpan.FromSeconds(20)));
        sut.Accept(Returned("message-1", body: large));
        sut.Accept(Returned("message-2", body: large));

        Assert.Contains("message-2", Assert.Single(Errors, text => text.Contains("too many")));

        release.SetResult();
        await sut.StoppedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_pass_should_end_with_a_message_that_cannot_be_stored_and_not_keep_the_dead_letters_waiting()
    {
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
               .Returns(_ => Interlocked.Increment(ref attempts) == 1
                   ? new ValueTask(firstAttempt.Task)
                   : ValueTask.FromException(new InvalidOperationException("the outbox is away")));
        var putBackWhenDeadLettering = -1;
        _deadLetterChannel
            .WhenForAnyArgs(channel => channel.BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default))
            .Do(_ => Interlocked.CompareExchange(ref putBackWhenDeadLettering, PutBackCount, -1));
        var sut = CreateSut(Period);

        // the worker is busy with the first one: the next two are handled in the same pass
        sut.Accept(Returned("message-0"));
        await WaitUntilAsync(() => PutBackCount == 1);
        sut.Accept(Returned("message-1", firstReturnedAt: Now - Period));
        sut.Accept(Returned("message-2"));
        firstAttempt.SetResult();
        await WaitUntilAsync(() => Volatile.Read(ref putBackWhenDeadLettering) >= 0);
        await sut.StoppedAsync(CancellationToken.None);

        // the one that failed was not read again, and again, before the dead letter went out
        Assert.Equal(2, putBackWhenDeadLettering);
    }

    [Fact]
    public async Task A_message_that_cannot_be_stored_once_the_application_is_stopping_should_be_reported_as_lost_without_waiting()
    {
        var attempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>()).Returns(_ => new ValueTask(attempt.Task));
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        await WaitUntilAsync(() => PutBackCount == 1);
        var stopped = sut.StoppedAsync(CancellationToken.None);
        attempt.SetException(new InvalidOperationException("the outbox is away"));
        await stopped;

        Assert.Contains("message-1", Assert.Single(Errors));
        Assert.False(IsLogged("could not be put back"));
    }

    [Fact]
    public async Task A_message_that_waits_to_be_tried_again_should_be_reported_as_lost_at_once_when_the_application_stops()
    {
        _clock.TimersFire = false;
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
               .Returns(_ => ValueTask.FromException(new InvalidOperationException("the outbox is away")));
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        await WaitUntilAsync(() => IsLogged("could not be put back"));
        await sut.StoppedAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Contains("message-1", Assert.Single(Errors));
        Assert.Equal(1, PutBackCount);
    }

    [Fact]
    public async Task A_message_should_not_be_stored_as_returned_before_it_was_created_when_the_clock_of_the_host_is_behind()
    {
        _clock.Now = CreatedAt.AddMinutes(-1);
        var sut = CreateSut(Period);

        sut.Accept(Returned("message-1"));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(CreatedAt, Assert.Single(_putBack).FirstReturnedAt);
    }

    private bool IsLogged(string text)
    {
        lock (_log)
            return _log.Any(entry => entry.Text.Contains(text));
    }

    // a message that can be read, followed by blanks: two of them fit in what can wait, three do not
    private static byte[] LargeBody()
    {
        var large = new byte[48 * 1024 * 1024];
        Array.Fill(large, (byte)' ');
        SerializedMessage.CopyTo(large, 0);
        return large;
    }

    [Fact]
    public async Task Messages_should_be_refused_and_reported_when_the_ones_that_are_waiting_are_too_large()
    {
        var large = LargeBody();
        var sut = CreateSut(Period);
        var release = HoldTheOutbox();

        // the one the worker has taken and not stored yet counts as well
        sut.Accept(Returned("message-0", body: large));
        await OutboxIsAskedAsync();
        sut.Accept(Returned("message-1", body: large));
        sut.Accept(Returned("message-2", body: large));

        Assert.Contains("message-2", Assert.Single(Errors, text => text.Contains("too many")));

        // what was refused took no room
        release.SetResult();
        await WaitUntilAsync(() => PutBackCount == 2);
        sut.Accept(Returned("message-3", body: large));
        await sut.StoppedAsync(CancellationToken.None);

        Assert.Equal(["message-0", "message-1", "message-3"], _putBack.Select(message => message.MessageId));
    }

    // the outbox does not answer until it is told to, or until whoever asks gives up
    private TaskCompletionSource HoldTheOutbox()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _outbox.PutBackAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
               .Returns(call => new ValueTask(HeldAsync(call.Arg<CancellationToken>())));
        return release;

        async Task HeldAsync(CancellationToken cancellationToken)
        {
            _outboxAsked.Release();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private readonly SemaphoreSlim _outboxAsked = new(0);

    private async Task OutboxIsAskedAsync()
        => Assert.True(await _outboxAsked.WaitAsync(TimeSpan.FromSeconds(20)), "the outbox was not asked to take a message back.");

    private static async Task WaitUntilAsync(Func<bool> reached)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!reached())
            await Task.Delay(10, timeout.Token);
    }

    // a channel that takes nothing, each time a new one
    private static IChannel FailingChannel()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        channel.BasicPublishAsync(string.Empty, string.Empty, default, default(BasicProperties)!, default, default)
               .ReturnsForAnyArgs(_ => ValueTask.FromException(new InvalidOperationException("it failed")));
        return channel;
    }

    private static ValueTask AssertDeadLetteredAsync(IChannel channel, string messageId)
        => channel.Received(1).BasicPublishAsync(
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

    private static readonly byte[] SerializedMessage = new JsonSerializer().Serialize(new FakeSagaStarter());

    private static BasicReturnEventArgs Returned(string messageId, DateTimeOffset? firstReturnedAt = null, byte[]? body = null)
        => new(312, "NO_ROUTE", Exchange, Exchange, Properties(messageId, firstReturnedAt), body ?? SerializedMessage, CancellationToken.None);

    private static BasicReturnEventArgs Unreadable(string messageId)
    {
        var properties = Properties(messageId, firstReturnedAt: null);
        properties.Headers![nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes("Not.A.Type");
        return new BasicReturnEventArgs(312, "NO_ROUTE", Exchange, Exchange, properties, SerializedMessage, CancellationToken.None);
    }

    // as the broker hands them back: the text of a header is bytes
    private static BasicProperties Properties(string messageId, DateTimeOffset? firstReturnedAt)
    {
        var headers = new Dictionary<string, object?>
        {
            [nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes(typeof(FakeSagaStarter).FullName!),
            [nameof(MessageEnvelope.SenderId)] = Encoding.UTF8.GetBytes("sender"),
            [nameof(MessageEnvelope.CreatedAt)] = Encoding.UTF8.GetBytes(CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
        };
        if (firstReturnedAt is not null)
            headers[nameof(MessageEnvelope.FirstReturnedAt)] = Encoding.UTF8.GetBytes(firstReturnedAt.Value.ToString("O", CultureInfo.InvariantCulture));

        return new BasicProperties { MessageId = messageId, CorrelationId = "correlation", Headers = headers };
    }

    /// <summary>the time the test says it is. What waits for a delay does not wait.</summary>
    private sealed class Clock : TimeProvider
    {
        private long _nowTicks;

        public DateTimeOffset Now
        {
            get => new(Interlocked.Read(ref _nowTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _nowTicks, value.UtcTicks);
        }

        public override DateTimeOffset GetUtcNow() => Now;

        /// <summary>when false, what waits for a delay waits until it is told to stop waiting.</summary>
        public bool TimersFire { get; set; } = true;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => base.CreateTimer(callback, state, TimersFire ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, period);
    }
}
