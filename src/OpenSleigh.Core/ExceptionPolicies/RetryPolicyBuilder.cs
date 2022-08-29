using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public class RetryPolicyBuilder : IRetryPolicyBuilder
    {
        internal RetryPolicyBuilder() { }

        public int MaxRetries { get; private set; } = 1;
        public IRetryPolicyBuilder WithMaxRetries(int value)
        {
            this.MaxRetries = value;
            return this;
        }

        private readonly List<ExceptionFilter> _exceptionFilters = new();
        public ExceptionFilters ExceptionFilters => new(_exceptionFilters);
        public IRetryPolicyBuilder Handle<TEx>() where TEx : Exception
        {
            _exceptionFilters.Add(ex => ex is TEx);
            return this;
        }

        public OnExceptionHandler OnExceptionHandler { get; private set; }
        public IRetryPolicyBuilder OnException(Func<ExceptionContext, Task> value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            this.OnExceptionHandler = new OnExceptionHandler(value);
            return this;
        }

        public IRetryPolicyBuilder OnException(Action<ExceptionContext> value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            Func<ExceptionContext, Task> wrapped = ctx =>
            {
                value(ctx);
                return Task.CompletedTask;
            };

            this.OnExceptionHandler = new OnExceptionHandler(wrapped);
            return this;
        }

        public DelayFactory DelayFactory { get; private set; } = RetryPolicy.DefaultDelayFactory;

        public IRetryPolicyBuilder WithDelay(DelayFactory value)
        {
            this.DelayFactory = value ?? throw new ArgumentNullException(nameof(value));
            return this;
        }

        public IPolicy Build() => new RetryPolicy(this.MaxRetries, this.ExceptionFilters, this.DelayFactory, this.OnExceptionHandler);
    }
}