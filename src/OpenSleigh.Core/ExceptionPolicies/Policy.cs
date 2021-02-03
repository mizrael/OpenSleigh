using System;

namespace OpenSleigh.Core.ExceptionPolicies
{
    internal static class Policy
    {
        public static PolicyBase Retry(Action<RetryPolicyBuilder> builderAction)
        {
            var builder = new RetryPolicyBuilder();
            builderAction(builder);
            return new RetryPolicy(builder.MaxRetries, builder.ExceptionFilters, builder.OnExceptionHandler);
        }
    }
}