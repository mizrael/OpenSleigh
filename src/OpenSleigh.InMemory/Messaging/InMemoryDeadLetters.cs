using OpenSleigh.Outbox;
using System.Collections.Concurrent;

namespace OpenSleigh.InMemory.Messaging;

/// <param name="Message">the message that could not be processed.</param>
/// <param name="Error">why it could not be processed.</param>
public record InMemoryDeadLetter(MessageEnvelope Message, Exception Error);

/// <summary>
/// where the in-memory transport puts the messages that could not be processed, and are not going to be processed again.
/// Registered as a singleton by <see cref="InMemoryBusConfiguratorExtensions.UseInMemoryTransport"/>.
/// </summary>
public class InMemoryDeadLetters
{
    /// <summary>
    /// how many messages are kept. When more arrive, the oldest are dropped.
    /// </summary>
    public const int Capacity = 1000;

    private readonly ConcurrentQueue<InMemoryDeadLetter> _messages = new();

    /// <summary>
    /// the dead-lettered messages, oldest first, as they are when the property is read.
    /// </summary>
    public IReadOnlyCollection<InMemoryDeadLetter> Messages => _messages.ToArray();

    internal void Add(MessageEnvelope message, Exception error)
    {
        _messages.Enqueue(new InMemoryDeadLetter(message, error));

        while (_messages.Count > Capacity)
            _messages.TryDequeue(out _);
    }
}
