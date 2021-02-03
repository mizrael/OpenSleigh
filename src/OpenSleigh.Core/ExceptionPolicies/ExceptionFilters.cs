using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenSleigh.Core.ExceptionPolicies
{
    internal delegate bool ExceptionFilter(Exception ex);
    
    internal class ExceptionFilters
    {
        private readonly IEnumerable<ExceptionFilter> _filters;

        public ExceptionFilters(IEnumerable<ExceptionFilter> filters)
        {
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
        }

        public bool CanHandle(Exception ex) => _filters.Any(p => p(ex));
    }
}