using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>one run of a handler: which message, for which saga instance, and when.</summary>
/// <param name="At">a <see cref="Stopwatch"/> timestamp, to compare with another one.</param>
public sealed record HandlerRun(Type MessageType, string MessageId, string CorrelationId, string InstanceId, long At);

/// <summary>
/// every run of the handlers of a scenario, on whatever host: it tells a message handled once from one handled
/// twice, which a count cannot. The sagas know nothing of it: <see cref="HandlerRunsExtensions.RecordHandlerRuns"/>
/// puts it in front of the handlers of a host. One instance for all the hosts.
/// </summary>
public sealed class HandlerRuns
{
    private readonly ConcurrentQueue<HandlerRun> _runs = new();
    private readonly List<HandlerHold> _holds = [];

    public IReadOnlyCollection<HandlerRun> Runs => _runs;

    /// <summary>how many different messages were given to a handler, each at least once.</summary>
    public int HandledMessages => _runs.Select(run => run.MessageId).Distinct().Count();

    public IReadOnlyList<HandlerRun> RunsOf<TMessage>()
        where TMessage : IMessage
        => _runs.Where(run => run.MessageType == typeof(TMessage)).OrderBy(run => run.At).ToArray();

    /// <summary>how many times each message was handled, by message id.</summary>
    public IReadOnlyDictionary<string, int> RunsByMessage()
        => _runs.GroupBy(run => run.MessageId).ToDictionary(runs => runs.Key, runs => runs.Count());

    /// <summary>
    /// keeps the next handler run that matches from starting until the scenario lets it go.
    /// Its saga is locked by then, and stays locked: this is how a scenario makes messages for one saga meet.
    /// </summary>
    public HandlerHold HoldNext(Func<HandlerRun, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var hold = new HandlerHold(matches);
        lock (_holds)
            _holds.Add(hold);
        return hold;
    }

    internal HandlerHold? Record(HandlerRun run)
    {
        _runs.Enqueue(run);

        lock (_holds)
        {
            var hold = _holds.Find(candidate => candidate.Matches(run));
            if (hold is not null)
                _holds.Remove(hold);
            return hold;
        }
    }
}

/// <summary>a handler run that waits for the scenario, with its saga locked.</summary>
public sealed class HandlerHold
{
    private readonly Func<HandlerRun, bool> _matches;
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal HandlerHold(Func<HandlerRun, bool> matches) => _matches = matches;

    /// <summary>completes when a handler run is being held.</summary>
    public Task Entered => _entered.Task;

    public void Release() => _released.TrySetResult();

    internal bool Matches(HandlerRun run) => _matches(run);

    internal Task WaitAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        return _released.Task.WaitAsync(cancellationToken);
    }
}

public static class HandlerRunsExtensions
{
    /// <summary>records every handler run of a host, and holds the ones the scenario asked to hold.</summary>
    public static IServiceCollection RecordHandlerRuns(this IServiceCollection services, HandlerRuns runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return services.Decorate<IMessageHandlerManager>((_, inner) => new RecordingHandlerManager(inner, runs));
    }

    // the handler manager is called for a saga that is locked and about to handle the message:
    // a message that is ignored, for a saga that is over or has handled it already, does not get here
    private sealed class RecordingHandlerManager(IMessageHandlerManager inner, HandlerRuns runs) : IMessageHandlerManager
    {
        public async ValueTask ProcessAsync<TM>(
            ISagaInstance executionContext,
            IMessageContext<TM> messageContext,
            CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            var hold = runs.Record(new HandlerRun(
                messageContext.Message.GetType(),
                messageContext.MessageId,
                messageContext.CorrelationId,
                executionContext.InstanceId,
                Stopwatch.GetTimestamp()));

            if (hold is not null)
                await hold.WaitAsync(cancellationToken);

            await inner.ProcessAsync(executionContext, messageContext, cancellationToken);
        }
    }
}
