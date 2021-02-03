using System;
using System.Collections.Generic;

namespace OpenSleigh.Core.ExceptionPolicies
{
    internal class RetryPolicyBuilder
    {
        internal RetryPolicyBuilder() { }

        public int MaxRetries { get; private set; } = 1;
        public RetryPolicyBuilder WithMaxRetries(int value)
        {
            this.MaxRetries = value;
            return this;
        }

        private readonly List<ExceptionFilter> _exceptionFilters = new();
        public ExceptionFilters ExceptionFilters => new ExceptionFilters(_exceptionFilters);
        public RetryPolicyBuilder Handle<TEx>() where TEx : Exception
        {
            _exceptionFilters.Add(ex => ex is TEx);
            return this;
        }
        
        public OnExceptionHandler OnExceptionHandler { get; private set; }
        public RetryPolicyBuilder OnException(OnExceptionHandler value)
        {
            this.OnExceptionHandler = value;
            return this;
        }
    }
}