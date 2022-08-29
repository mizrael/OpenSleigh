using System;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Core.ExceptionPolicies
{
    [ExcludeFromCodeCoverage]
    public record ExceptionContext(Exception Exception, int ExecutionIndex);
}