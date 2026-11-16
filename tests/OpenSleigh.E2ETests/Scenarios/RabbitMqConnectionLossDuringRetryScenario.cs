using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;
using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connections of the hosts to RabbitMQ are closed at the moment a host sends a message, whose saga is locked,
/// to the retry queue: the message is neither in its queue nor in the retry queue if the host has let go of it
/// before the retry queue has it. It has to be handled all the same, once.
/// </summary>
public class RabbitMqConnectionLossDuringRetryScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int Movements = 12;
    private const int Messages = 1 + Movements;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    // three hosts: one keeps the saga, and the movements that the other two are given find it locked
    [Theory]
    [MatrixData(Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq], HostsCounts = [3])]
    public async Task a_message_whose_retry_meets_a_closed_connection_is_handled_once(string combination, int hostsCount)
    {
        await using var connectionLog = new RabbitMqConnectionLog(Console);
        var account = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();
        var retries = new RetryPublishes();

        var setup = new HostSetup(bus => bus.AddSaga<AccountSaga, AccountState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .TrackRabbitMqConnections(connectionLog)
                .Decorate<IChannelFactory>((host, inner) => new InterceptingChannelFactory(inner, RetryExchangesOf(host), retries))
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Of(connectionLog);

                await run.AnyHost.PublishNewAsync(new OpenAccount(account), cancellationToken);
                await run.WaitForReleasedSagaAsync<AccountSaga>(account, handledMessages: 1, cancellationToken);

                var open = await run.WaitForAsync("the broker to list the connections of the hosts",
                    listing => connections.WaitForAsync(hostsCount, listing), cancellationToken);

                // the first host to send a movement to the retry queue loses its connection before the broker is
                // given it. The relays have published every movement by then: a publish that meets a connection
                // being closed is another matter, and can be lost
                retries.BeforeTheNext(async channel =>
                {
                    while (await run.CountOutboxMessagesAsync(cancellationToken) > 0)
                        await Task.Delay(PollingInterval, cancellationToken);

                    await connections.CloseAsync(open, cancellationToken);
                    while (channel.IsOpen)
                        await Task.Delay(PollingInterval, cancellationToken);
                });

                await run.ForceLockConflictAsync(runs,
                    handlerRun => handlerRun.CorrelationId == account,
                    () => Task.WhenAll(MovementsOf(account).Select((movement, index)
                        => run.Hosts[index % run.Hosts.Count].PublishNewAsync(movement, cancellationToken))),
                    cancellationToken);

                await run.WaitForAsync("a host to send a movement to the retry queue on a connection that was closed",
                    retries.Intercepted, cancellationToken);
                await run.WaitUntilAsync($"the {Messages} messages to be handled",
                    () => runs.HandledMessages == Messages, cancellationToken);

                var stored = await run.WaitForReleasedSagaAsync<AccountSaga>(account, Messages, cancellationToken);
                var state = Assert.IsType<AccountState>(stored.StateData);
                Assert.Equal(Movements, state.Movements);
                Assert.Equal(MovementsOf(account).Sum(AmountOf), state.Balance);
            },
            AssertOnlyLockConflictsAsync);

        // a movement that is in its queue and in the retry queue is delivered twice, and handled once
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

    /// <summary>what the scenario does before a host publishes to a retry exchange, once.</summary>
    private sealed class RetryPublishes
    {
        private readonly TaskCompletionSource _intercepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<IChannel, Task>? _beforeTheNext;

        /// <summary>completes when a publish to a retry exchange was held back, and what the scenario does before it is done.</summary>
        public Task Intercepted => _intercepted.Task;

        /// <param name="act">is given the channel the host is about to publish on.</param>
        public void BeforeTheNext(Func<IChannel, Task> act) => Volatile.Write(ref _beforeTheNext, act);

        internal Func<IChannel, Task>? Take() => Interlocked.Exchange(ref _beforeTheNext, null);

        internal void Done() => _intercepted.TrySetResult();
    }

    private sealed class InterceptingChannelFactory(IChannelFactory inner, IReadOnlySet<string> retryExchanges, RetryPublishes retries) : IChannelFactory
    {
        // the subscriber is given the same channel for as long as the factory gives it the same one
        private readonly ConditionalWeakTable<IChannel, IChannel> _intercepted = new();

        public ValueTask<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken = default)
            => inner.GetPublishChannelAsync(cancellationToken);

        public async ValueTask<IChannel> GetConsumeChannelAsync(CancellationToken cancellationToken = default)
        {
            var channel = await inner.GetConsumeChannelAsync(cancellationToken);
            return _intercepted.GetValue(channel, consuming => InterceptedChannel.Of(consuming, retryExchanges, retries));
        }
    }

    /// <summary>a channel that does what the one it wraps does, except for one publish to a retry exchange.</summary>
    private class InterceptedChannel : DispatchProxy
    {
        private const int ExchangeArgument = 0;

        private IChannel _channel = null!;
        private IReadOnlySet<string> _retryExchanges = null!;
        private RetryPublishes _retries = null!;

        public static IChannel Of(IChannel channel, IReadOnlySet<string> retryExchanges, RetryPublishes retries)
        {
            var intercepted = Create<IChannel, InterceptedChannel>();
            var interception = (InterceptedChannel)(object)intercepted;
            interception._channel = channel;
            interception._retryExchanges = retryExchanges;
            interception._retries = retries;
            return intercepted;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name == nameof(IChannel.BasicPublishAsync)
                && args?[ExchangeArgument] is string exchange
                && _retryExchanges.Contains(exchange)
                && _retries.Take() is { } beforeThePublish)
                return new ValueTask(PublishAfterAsync(beforeThePublish, targetMethod, args));

            return Forward(targetMethod, args);
        }

        private async Task PublishAfterAsync(Func<IChannel, Task> beforeThePublish, MethodInfo publish, object?[] args)
        {
            try
            {
                await beforeThePublish(_channel);
                await (ValueTask)Forward(publish, args)!;
            }
            finally
            {
                _retries.Done();
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
