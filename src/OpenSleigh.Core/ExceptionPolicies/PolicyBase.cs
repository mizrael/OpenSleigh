using System;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public delegate Task OnExceptionHandler(ExceptionContext ctx);

    public abstract class PolicyBase
    {
        private readonly OnExceptionHandler _onExceptionHandler;
        private static readonly OnExceptionHandler DefaultExceptionHandler = new(_ => Task.CompletedTask);
        private readonly ExceptionFilters _exceptionFilters;
        
        protected PolicyBase(ExceptionFilters exceptionFilters, OnExceptionHandler onExceptionHandler = null)
        {
            _exceptionFilters = exceptionFilters ?? throw new ArgumentNullException(nameof(exceptionFilters));

            _onExceptionHandler = onExceptionHandler ?? DefaultExceptionHandler;
        }

        public abstract Task<TRes> WrapAsync<TRes>(Func<Task<TRes>> action);

        protected bool CanHandle(Exception ex) => _exceptionFilters?.CanHandle(ex) ?? false;

        protected async Task OnException(ExceptionContext ctx)
        {
            await _onExceptionHandler.Invoke(ctx).ConfigureAwait(false);
        }
    }
}