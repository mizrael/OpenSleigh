using System;

namespace OpenSleigh.Core.ExceptionPolicies
{
    internal static class Policy
    {
        public static IPolicy Retry(Action<RetryPolicyBuilder> builderAction)
        {
            var builder = new RetryPolicyBuilder();
            builderAction(builder);
            return builder.Build();
        }
    }
}