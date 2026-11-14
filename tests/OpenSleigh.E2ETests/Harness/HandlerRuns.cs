using System.Collections.Concurrent;
using System.Diagnostics;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>one run of a handler: which message, for which saga instance, and when.</summary>
/// <param name="At">a <see cref="Stopwatch"/> timestamp, to compare with another one.</param>
public sealed record HandlerRun(Type MessageType, string MessageId, string CorrelationId, string InstanceId, long At);

/// <summary>
/// every run of the handlers of a scenario, from whatever host and thread they run on. A saga records its runs here
/// when the scenario has to tell a message handled once from one handled twice, which a count cannot.
/// Registered as a singleton in <see cref="HostSetup.ConfigureServices"/>, one instance for all the hosts.
/// </summary>
public sealed class HandlerRuns
{
    private readonly int _expectedMessages;
    private readonly ConcurrentQueue<HandlerRun> _runs = new();
    private readonly ConcurrentDictionary<string, byte> _messageIds = new();
    private readonly TaskCompletionSource _allHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="expectedMessages">how many different messages the scenario expects to be handled.</param>
    public HandlerRuns(int expectedMessages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedMessages, 1);
        _expectedMessages = expectedMessages;
    }

    public IReadOnlyCollection<HandlerRun> Runs => _runs;

    /// <summary>completes once as many different messages as expected were handled, each at least once.</summary>
    public Task AllHandled => _allHandled.Task;

    public void Record<TMessage>(IMessageContext<TMessage> context, ISagaInstance instance)
        where TMessage : IMessage
    {
        _runs.Enqueue(new HandlerRun(
            context.Message.GetType(), context.MessageId, context.CorrelationId, instance.InstanceId, Stopwatch.GetTimestamp()));

        _messageIds.TryAdd(context.MessageId, 0);
        if (_messageIds.Count >= _expectedMessages)
            _allHandled.TrySetResult();
    }

    public IReadOnlyList<HandlerRun> RunsOf<TMessage>()
        where TMessage : IMessage
        => _runs.Where(run => run.MessageType == typeof(TMessage)).OrderBy(run => run.At).ToArray();

    /// <summary>how many times each message was handled, by message id.</summary>
    public IReadOnlyDictionary<string, int> RunsByMessage()
        => _runs.GroupBy(run => run.MessageId).ToDictionary(runs => runs.Key, runs => runs.Count());
}
