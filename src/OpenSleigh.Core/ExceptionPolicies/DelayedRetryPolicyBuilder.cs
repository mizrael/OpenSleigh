namespace OpenSleigh.Core.ExceptionPolicies
{
    internal class DelayedRetryPolicyBuilder : RetryPolicyBuilder
    {
        internal DelayedRetryPolicyBuilder() { }

        public DelayFactory DelayFactory { get; private set; }

        public DelayedRetryPolicyBuilder WithDelayFactory(DelayFactory value)
        {
            this.DelayFactory = value;
            return this;
        }
    }
}