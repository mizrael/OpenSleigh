namespace OpenSleigh.Outbox;

public record OutboxProcessorOptions(TimeSpan Interval)
{
    public static readonly OutboxProcessorOptions Default = new(TimeSpan.FromSeconds(5));

    /// <summary>
    /// when true, the relay reads the outbox again right away for as long as the previous read relayed something,
    /// and only waits for <see cref="Interval"/> once it finds nothing to relay.
    /// When false it waits for <see cref="Interval"/> after every read. Defaults to true.
    /// </summary>
    public bool DrainPendingMessages { get; init; } = true;

    /// <summary>
    /// when true, messages appended to the outbox by this process wake the relay up immediately.
    /// When false they wait for the next poll. Messages appended by other processes always wait for the next poll.
    /// Defaults to true.
    /// </summary>
    public bool WakeOnPublish { get; init; } = true;
}