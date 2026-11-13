using Confluent.Kafka;
using System.Text;

namespace OpenSleigh.Transport.Kafka;

/// <summary>
/// what a consumer needs to know about the retry topics it reads: which topics they are, which of their messages
/// are its own, and when one of them can be processed.
/// </summary>
internal sealed class RetryTopics
{
    private readonly HashSet<string> _topicNames;
    private readonly byte[]? _clientGroup;
    private readonly TimeSpan _maxDelay;

    /// <param name="clientGroup">the group of this consumer. When missing, every retried message is taken as its own.</param>
    /// <param name="maxDelay">the longest a message is kept waiting, whatever its not-before time says.</param>
    public RetryTopics(IEnumerable<string> topicNames, string? clientGroup, TimeSpan maxDelay)
    {
        _topicNames = new HashSet<string>(topicNames, StringComparer.Ordinal);
        _clientGroup = string.IsNullOrEmpty(clientGroup) ? null : Encoding.UTF8.GetBytes(clientGroup);
        _maxDelay = maxDelay;
    }

    public bool Contains(string topicName) => _topicNames.Contains(topicName);

    public static Header CreateGroupHeader(string clientGroup)
        => new(HeaderNames.RetryGroup, Encoding.UTF8.GetBytes(clientGroup));

    /// <summary>
    /// a retry topic is shared by every group that handles the message type: a message in it
    /// is processed again only by the group that could not process it.
    /// </summary>
    public bool IsForAnotherGroup(Headers? headers)
    {
        if (_clientGroup is null || headers is null)
            return false;

        return headers.TryGetLastBytes(HeaderNames.RetryGroup, out var group)
               && group is not null
               && !group.AsSpan().SequenceEqual(_clientGroup);
    }

    /// <summary>
    /// the moment a message of a retry topic can be processed: its not-before time, but never further away
    /// than the longest delay. A message without a readable not-before time can be processed straight away.
    /// </summary>
    public DateTimeOffset DueAt(Headers? headers, DateTimeOffset now)
    {
        if (!RetrySchedule.TryGetNotBefore(headers, out var notBefore))
            return now;

        var latest = now + _maxDelay;
        return notBefore < latest ? notBefore : latest;
    }
}
