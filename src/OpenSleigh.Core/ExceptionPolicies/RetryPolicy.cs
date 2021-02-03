using System;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    internal class RetryPolicy : PolicyBase
    {
        private readonly int _maxRetries = 3;

        public RetryPolicy(int maxRetries, ExceptionFilters exceptionFilters, OnExceptionHandler onException = null) : 
            base(exceptionFilters, onException)
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
    }
}