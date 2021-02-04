using System;

namespace OpenSleigh.Core.ExceptionPolicies
{
    internal class DelayedRetryPolicyBuilder : RetryPolicyBuilder
    {
        internal DelayedRetryPolicyBuilder() { }

        public DelayFactory DelayFactory { get; private set; } = new (i => TimeSpan.Zero);

        public DelayedRetryPolicyBuilder WithDelay(DelayFactory value)
        {
            this.DelayFactory = value ?? throw new ArgumentNullException(nameof(value));
            return this;
        }
    }
}