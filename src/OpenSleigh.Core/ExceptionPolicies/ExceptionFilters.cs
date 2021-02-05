using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public delegate bool ExceptionFilter(Exception ex);

    public class ExceptionFilters
    {
        private readonly IEnumerable<ExceptionFilter> _filters;

        public ExceptionFilters(IEnumerable<ExceptionFilter> filters)
        {
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
        }

        public bool CanHandle(Exception ex) => _filters.Any(p => p(ex));
    }
}