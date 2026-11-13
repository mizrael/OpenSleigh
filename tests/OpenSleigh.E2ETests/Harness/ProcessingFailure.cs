using System.Collections.Concurrent;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// a message that a host gave back to its transport with an exception: what the transport then does with it
/// depends on the exception. A lock conflict is retried, anything else is dead-lettered.
/// </summary>
public sealed record ProcessingFailure(string Service, Type MessageType, string MessageId, Exception Exception)
{
    private const string SagaNotFoundMessagePrefix = "unable to locate state for Saga";

    /// <summary>
    /// the message is for a saga that is not stored, and does not start it. By design it is dead-lettered
    /// at once and never retried, also when the saga is only late: a message that starts several sagas is handled
    /// by one after the other, and what the first publishes can reach another host before the next one is stored.
    /// </summary>
    public bool IsSagaNotFound
        => Exception is ApplicationException && Exception.Message.StartsWith(SagaNotFoundMessagePrefix, StringComparison.Ordinal);

    /// <summary>the saga was locked by someone else: the message is retried, by the service that could not process it.</summary>
    public bool IsLockConflict => Exception.IsLockConflict();

    /// <summary>a handler threw, in one saga or in several: the message is dead-lettered once every saga had its turn.</summary>
    public bool IsHandlerFailure
        => Exception is SagaException
           || Exception is AggregateException aggregate && aggregate.InnerExceptions.All(inner => inner is SagaException);
}

/// <summary>
/// wraps the message processor of a host, outside any decorator of the scenario, and keeps what it throws.
/// </summary>
internal sealed class RecordingMessageProcessor(
    IMessageProcessor inner,
    string service,
    ConcurrentQueue<ProcessingFailure> failures) : IMessageProcessor
{
    public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            failures.Enqueue(new ProcessingFailure(service, outboxMessage.MessageType, outboxMessage.MessageId, e));
            throw;
        }
    }
}
