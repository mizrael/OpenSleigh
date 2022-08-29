using System;

namespace OpenSleigh.Core.Messaging
{
    public record OutboxCleanerOptions(TimeSpan Interval)
    {
        public static readonly OutboxCleanerOptions Default = new OutboxCleanerOptions(TimeSpan.FromSeconds(5));
    }

}