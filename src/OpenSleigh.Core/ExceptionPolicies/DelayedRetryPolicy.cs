using System;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    internal delegate TimeSpan DelayFactory(int executionIndex);
    
    internal class DelayedRetryPolicy : RetryPolicy
    {
        public static readonly DelayFactory DefaultDelayFactory = new(i => TimeSpan.Zero);
        
        public DelayedRetryPolicy(int maxRetries,
            ExceptionFilters exceptionFilters,
            DelayFactory delayFactory,
            OnExceptionHandler onException = null) : base(maxRetries, exceptionFilters, OnExceptionWrapper(delayFactory, onException))
        {
        }

        private static OnExceptionHandler OnExceptionWrapper(DelayFactory delayFactory,
            OnExceptionHandler action)
        {
            delayFactory ??= DefaultDelayFactory;

            OnExceptionHandler res = async (ctx) =>
            {
                var delay = delayFactory(ctx.ExecutionIndex);
                await Task.Delay(delay).ConfigureAwait(false);
                if(action is not null)
                    await action(ctx).ConfigureAwait(false);
            };
            return res;
        }
    }
}