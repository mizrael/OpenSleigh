using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connections of the hosts to RabbitMQ are closed while a host sends a message whose saga is locked to the
/// retry queue, on either side of its publish to the retry exchange. It has to be handled, once.
/// </summary>
public class RabbitMqConnectionLossDuringRetryScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int Movements = 12;
    private const int Messages = 1 + Movements;
    private const int DeliveriesOfADuplicate = 2;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    private enum Moment
    {
        BeforeThePublish,
        BeforeTheAcknowledgement,
    }

    // one store: it is the broker that goes away here, and the store takes no part in it.
    // Three hosts: one keeps the saga, and the movements that the other two are given find it locked.
    // Acknowledged before it is published, the message is in no queue when its publish fails
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [3])]
    public Task a_message_whose_retry_meets_a_closed_connection_is_handled_once(string combination, int hostsCount)
        => RunAsync(combination, hostsCount, Moment.BeforeThePublish);

    // the retry queue has its copy and its queue gives it back: it is delivered twice
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [3])]
    public Task a_message_that_is_in_the_retry_queue_and_could_not_be_acknowledged_is_handled_once(string combination, int hostsCount)
        => RunAsync(combination, hostsCount, Moment.BeforeTheAcknowledgement);

    private async Task RunAsync(string combination, int hostsCount, Moment moment)
    {
        await using var connectionLog = new RabbitMqConnectionLog(Console);
        var account = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();
        var retry = new RetryInterception(moment);
        var deliveries = new CompletedDeliveries();

        var setup = new HostSetup(bus => bus.AddSaga<AccountSaga, AccountState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .TrackRabbitMqConnections(connectionLog)
                .Decorate<IMessageProcessor>((_, inner) => new CountingMessageProcessor(inner, deliveries))
                .Decorate<IChannelFactory>((host, inner) => new InterceptingChannelFactory(inner, RetryExchangesOf(host), retry))
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Of(connectionLog);

                await run.AnyHost.PublishNewAsync(new OpenAccount(account), cancellationToken);
                await run.WaitForReleasedSagaAsync<AccountSaga>(account, handledMessages: 1, cancellationToken);

                var open = await run.WaitForAsync("the broker to list the connections of the hosts",
                    listing => connections.WaitForAsync(hostsCount, listing), cancellationToken);

                retry.CloseTheConnectionsWith(async channel =>
                {
                    // the relays have published every movement by then: a publish on a closing connection can be lost
                    while (await run.CountOutboxMessagesAsync(cancellationToken) > 0)
                        await Task.Delay(PollingInterval, cancellationToken);

                    // the client opens the channel again by itself: that it is open tells nothing, that it was shut does
                    var shut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    channel.ChannelShutdownAsync += (_, _) =>
                    {
                        shut.TrySetResult();
                        return Task.CompletedTask;
                    };

                    await connections.CloseAsync(open, cancellationToken);
                    await shut.Task.WaitAsync(cancellationToken);
                });

                // the first movement to get the saga keeps it until the retry of another one has met the closed connection
                var first = runs.HoldNext(handlerRun => handlerRun.CorrelationId == account);
                try
                {
                    await Task.WhenAll(MovementsOf(account).Select((movement, index)
                        => run.Hosts[index % run.Hosts.Count].PublishNewAsync(movement, cancellationToken)));

                    await run.WaitForAsync("one of the movements to get the account", first.Entered, cancellationToken);
                    await run.WaitForAsync("a host to send a movement to the retry queue while its connection is closed",
                        retry.Done, cancellationToken);
                }
                finally
                {
                    first.Release();
                }

                await run.WaitUntilAsync($"the {Messages} messages to be handled",
                    () => runs.HandledMessages == Messages, cancellationToken);

                if (moment == Moment.BeforeTheAcknowledgement)
                    await run.WaitUntilAsync("the movement to be delivered from its queue and from the retry queue",
                        () => deliveries.Of(retry.MessageId!) >= DeliveriesOfADuplicate, cancellationToken);

                var stored = await run.WaitForReleasedSagaAsync<AccountSaga>(account, Messages, cancellationToken);
                var state = Assert.IsType<AccountState>(stored.StateData);
                Assert.Equal(Movements, state.Movements);
                Assert.Equal(MovementsOf(account).Sum(AmountOf), state.Balance);
            },
            AssertOnlyLockConflictsAsync);

        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
    }

    // deposits and withdrawals have a queue each: they reach the hosts independently.
    // No two amounts are the same, so a movement applied twice cannot make up for one that was lost
    private static IEnumerable<IMessage> MovementsOf(string account)
        => Enumerable.Range(0, Movements).Select<int, IMessage>(index => index % 2 == 0
            ? new Deposit(account, 1 << index)
            : new Withdraw(account, 1 << index));

    private static int AmountOf(IMessage movement)
        => movement is Deposit deposit ? deposit.Amount : -((Withdraw)movement).Amount;

    private static IReadOnlySet<string> RetryExchangesOf(IServiceProvider host)
    {
        var references = host.GetRequiredService<IQueueReferenceFactory>();
        return host.GetHandledMessageTypes()
            .Select(messageType => references.Create(messageType).RetryExchangeName)
            .ToHashSet();
    }

    /// <summary>
    /// how many deliveries of a message a host was done with, without giving it back to be retried. A message
    /// that is in one queue only gets there once: twice means that it was delivered from two.
    /// </summary>
    private sealed class CompletedDeliveries
    {
        private readonly ConcurrentDictionary<string, int> _byMessage = new();

        public int Of(string messageId) => _byMessage.GetValueOrDefault(messageId);

        internal void Add(string messageId) => _byMessage.AddOrUpdate(messageId, 1, (_, count) => count + 1);
    }

    private sealed class CountingMessageProcessor(IMessageProcessor inner, CompletedDeliveries deliveries) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);
            deliveries.Add(outboxMessage.MessageId);
        }
    }

    /// <summary>the first retry of the scenario, during which the connections are closed, once.</summary>
    private sealed class RetryInterception(Moment moment)
    {
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<IChannel, Task>? _closeTheConnections;
        private string? _messageId;

        public Moment Moment => moment;

        /// <summary>the message whose retry was intercepted, once there is one.</summary>
        public string? MessageId => Volatile.Read(ref _messageId);

        /// <summary>completes once the connections were closed and the host has gone on with the retry.</summary>
        public Task Done => _done.Task;

        /// <param name="close">is given the channel the host retries on, and returns once that channel was shut.</param>
        public void CloseTheConnectionsWith(Func<IChannel, Task> close) => Volatile.Write(ref _closeTheConnections, close);

        internal Func<IChannel, Task>? TakeFor(string? messageId)
        {
            var close = Interlocked.Exchange(ref _closeTheConnections, null);
            if (close is not null)
                Volatile.Write(ref _messageId, messageId);
            return close;
        }

        internal void Complete() => _done.TrySetResult();
    }

    private sealed class InterceptingChannelFactory(IChannelFactory inner, IReadOnlySet<string> retryExchanges, RetryInterception retry) : IChannelFactory
    {
        // the subscriber is given the same channel for as long as the factory gives it the same one
        private readonly ConditionalWeakTable<IChannel, IChannel> _intercepted = new();

        public ValueTask<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken = default)
            => inner.GetPublishChannelAsync(cancellationToken);

        public async ValueTask<IChannel> GetConsumeChannelAsync(CancellationToken cancellationToken = default)
        {
            var channel = await inner.GetConsumeChannelAsync(cancellationToken);
            return _intercepted.GetValue(channel, consuming => InterceptedChannel.Of(consuming, retryExchanges, retry));
        }
    }

    /// <summary>
    /// a channel that does what the one it wraps does, and closes the connections around one publish to a retry
    /// exchange: before it, or between it and the acknowledgement that follows on the same channel.
    /// </summary>
    private class InterceptedChannel : DispatchProxy
    {
        private const int ExchangeArgument = 0;
        private const int RoutingKeyArgument = 1;
        private const int PropertiesArgument = 3;

        private IChannel _channel = null!;
        private IReadOnlySet<string> _retryExchanges = null!;
        private RetryInterception _retry = null!;
        private Func<IChannel, Task>? _beforeTheNextAcknowledgement;

        public static IChannel Of(IChannel channel, IReadOnlySet<string> retryExchanges, RetryInterception retry)
        {
            var intercepted = Create<IChannel, InterceptedChannel>();
            var interception = (InterceptedChannel)(object)intercepted;
            interception._channel = channel;
            interception._retryExchanges = retryExchanges;
            interception._retry = retry;
            return intercepted;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name == nameof(IChannel.BasicPublishAsync)
                && args?[ExchangeArgument] is string exchange
                && _retryExchanges.Contains(exchange)
                && _retry.TakeFor((args[PropertiesArgument] as IReadOnlyBasicProperties)?.MessageId) is { } close)
                return new ValueTask(PublishAsync(close, targetMethod, args));

            if (targetMethod.Name == nameof(IChannel.BasicAckAsync)
                && Interlocked.Exchange(ref _beforeTheNextAcknowledgement, null) is { } closeBeforeTheAcknowledgement)
                return new ValueTask(AfterClosingAsync(closeBeforeTheAcknowledgement, targetMethod, args));

            return Forward(targetMethod, args);
        }

        private async Task PublishAsync(Func<IChannel, Task> close, MethodInfo publish, object?[] args)
        {
            if (_retry.Moment == Moment.BeforeThePublish)
            {
                await AfterClosingAsync(close, publish, args);
                return;
            }

            await (ValueTask)Forward(publish, args)!;

            // the broker answers what a channel asks in order: once it has answered this, it has taken the publish
            await _channel.QueueDeclarePassiveAsync((string)args[RoutingKeyArgument]!);
            Volatile.Write(ref _beforeTheNextAcknowledgement, close);
        }

        private async Task AfterClosingAsync(Func<IChannel, Task> close, MethodInfo method, object?[]? args)
        {
            try
            {
                await close(_channel);
                await (ValueTask)Forward(method, args)!;
            }
            finally
            {
                _retry.Complete();
            }
        }

        private object? Forward(MethodInfo method, object?[]? args)
        {
            try
            {
                return method.Invoke(_channel, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }
}
