using System;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public interface IRetryPolicyBuilder : IPolicyBuilder
    {
        IRetryPolicyBuilder Handle<TEx>() where TEx : Exception;
        IRetryPolicyBuilder OnException(Action<ExceptionContext> value);
        IRetryPolicyBuilder OnException(Func<ExceptionContext, Task> value);
        IRetryPolicyBuilder WithDelay(DelayFactory value);
        IRetryPolicyBuilder WithMaxRetries(int value);
    }
}