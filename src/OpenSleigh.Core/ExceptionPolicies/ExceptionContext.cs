using System;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Core.ExceptionPolicies
{
    [ExcludeFromCodeCoverage]
    internal record ExceptionContext(Exception Exception, int ExecutionIndex);
}