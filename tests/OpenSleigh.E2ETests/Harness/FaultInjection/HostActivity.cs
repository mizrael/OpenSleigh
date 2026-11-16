using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// what each host of a scenario did, and when: the passes of its relay, what it published, what it was delivered
/// and processed. It tells a host that works again from one whose work the others are doing: hosts of a service
/// share their outbox and their queues, and a count over all of them cannot. One instance for all the hosts.
/// </summary>
public sealed class HostActivity
{
    private readonly record struct RelayPass(object Host, long StartedAt, long EndedAt, bool Failed);

    private readonly record struct Event(object Host, long At);

    private readonly ConcurrentQueue<RelayPass> _relayPasses = new();
    private readonly ConcurrentQueue<Event> _published = new();
    private readonly ConcurrentQueue<Event> _processed = new();
    private long _since = Stopwatch.GetTimestamp();

    /// <summary>from now on, the questions about a host are about what it does from now on.</summary>
    public void StartOver() => Interlocked.Exchange(ref _since, Stopwatch.GetTimestamp());

    /// <summary>true when the relay of the host published a message.</summary>
    public bool HasPublished(ScenarioHost host) => Any(_published, host);

    /// <summary>true when the host was delivered a message and processed it without failing.</summary>
    public bool HasProcessed(ScenarioHost host) => Any(_processed, host);

    /// <summary>true when the relay of the host read its outbox and left nothing behind that it could not relay.</summary>
    public bool HasRelayed(ScenarioHost host)
        => RelayPassesOf(host).Any(pass => !pass.Failed);

    /// <summary>how many passes of the relay of the host could not read the outbox, or left a message in it.</summary>
    public int FailedRelayPasses(ScenarioHost host)
        => RelayPassesOf(host).Count(pass => pass.Failed);

    /// <summary>
    /// the shortest time a relay let pass between a pass that failed and its next one, whatever became of that one:
    /// how long it backs off. Null when no relay had a pass after one that failed.
    /// </summary>
    public TimeSpan? ShortestPauseAfterAFailedRelayPass()
    {
        var pauses = from passes in _relayPasses.GroupBy(pass => pass.Host)
                     let inOrder = passes.OrderBy(pass => pass.StartedAt).ToArray()
                     from index in Enumerable.Range(1, Math.Max(0, inOrder.Length - 1))
                     where inOrder[index - 1].Failed
                     select Stopwatch.GetElapsedTime(inOrder[index - 1].EndedAt, inOrder[index].StartedAt);

        return pauses.Cast<TimeSpan?>().Min();
    }

    private IEnumerable<RelayPass> RelayPassesOf(ScenarioHost host)
    {
        var identity = IdentityOf(host.Services);
        var since = Interlocked.Read(ref _since);
        return _relayPasses.Where(pass => ReferenceEquals(pass.Host, identity) && pass.StartedAt >= since);
    }

    private bool Any(ConcurrentQueue<Event> events, ScenarioHost host)
    {
        var identity = IdentityOf(host.Services);
        var since = Interlocked.Read(ref _since);
        return events.Any(happened => ReferenceEquals(happened.Host, identity) && happened.At >= since);
    }

    // one per host, whatever scope the provider is
    private static object IdentityOf(IServiceProvider hostServices) => hostServices.GetRequiredService<ISystemInfo>();

    internal IPublisher Observe(IServiceProvider hostServices, IPublisher inner) => new Publisher(inner, IdentityOf(hostServices), _published);

    internal IMessageProcessor Observe(IServiceProvider hostServices, IMessageProcessor inner) => new Processor(inner, IdentityOf(hostServices), _processed);

    internal IOutboxProcessor Observe(IServiceProvider hostServices, IOutboxProcessor inner) => new Relay(inner, IdentityOf(hostServices), _relayPasses);

    private sealed class Publisher(IPublisher inner, object host, ConcurrentQueue<Event> published) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await inner.PublishAsync(envelope, cancellationToken);
            published.Enqueue(new Event(host, Stopwatch.GetTimestamp()));
        }
    }

    private sealed class Processor(IMessageProcessor inner, object host, ConcurrentQueue<Event> processed) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);
            processed.Enqueue(new Event(host, Stopwatch.GetTimestamp()));
        }
    }

    private sealed class Relay(IOutboxProcessor inner, object host, ConcurrentQueue<RelayPass> passes) : IOutboxProcessor
    {
        public ValueTask ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
            => inner.ProcessPendingMessagesAsync(cancellationToken);

        public async ValueTask<OutboxRelayResult> RelayPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                var result = await inner.RelayPendingMessagesAsync(cancellationToken);
                passes.Enqueue(new RelayPass(host, startedAt, Stopwatch.GetTimestamp(), result.Failed > 0));

                return result;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                passes.Enqueue(new RelayPass(host, startedAt, Stopwatch.GetTimestamp(), Failed: true));
                throw;
            }
        }
    }
}

public static class HostActivityExtensions
{
    /// <summary>records what a host does. Call it from <see cref="HostSetup.ConfigureServices"/>.</summary>
    public static IServiceCollection RecordHostActivity(this IServiceCollection services, HostActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return services
            .Decorate<IPublisher>(activity.Observe)
            .Decorate<IMessageProcessor>(activity.Observe)
            .Decorate<IOutboxProcessor>(activity.Observe);
    }
}
