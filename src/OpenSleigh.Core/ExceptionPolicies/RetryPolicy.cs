using System;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public class RetryPolicy : PolicyBase
    {
        public static readonly DelayFactory DefaultDelayFactory = new(i => TimeSpan.Zero);

        private readonly int _maxRetries;

        public RetryPolicy(int maxRetries, 
            ExceptionFilters exceptionFilters,
            DelayFactory delayFactory = null,
            OnExceptionHandler onException = null) : 
            base(exceptionFilters, OnExceptionWrapper(delayFactory, onException))
        {
            _maxRetries = maxRetries;
        }

        public override async Task<TRes> WrapAsync<TRes>(Func<Task<TRes>> action)
        {
            int count = 0;
            
            Exception lastException = null;
            
            while (count < _maxRetries)
            {
                try
                {
                    var res = await action().ConfigureAwait(false);
                    return res;
                }
                catch (Exception e)
                {
                    lastException = e;
                    await OnException(new ExceptionContext(e, count)).ConfigureAwait(false);

                    count++;

                    if (CanHandle(e))
                        continue;

                    throw;
                }
            }

            if(lastException is not null)
                throw lastException;

            return default;
        }

        private static OnExceptionHandler OnExceptionWrapper(DelayFactory delayFactory,
            OnExceptionHandler action)
        {
            delayFactory ??= DefaultDelayFactory;

            OnExceptionHandler res = async (ctx) =>
            {
                var delay = delayFactory(ctx.ExecutionIndex);
                await Task.Delay(delay).ConfigureAwait(false);
                if (action is not null)
                    await action(ctx).ConfigureAwait(false);
            };
            return res;
        }
    }
}