namespace OpenSleigh.Outbox
{
    public record OutboxProcessorOptions(TimeSpan Interval)
    {
        public static readonly OutboxProcessorOptions Default = new(TimeSpan.FromSeconds(5));
    }
}