using System;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public delegate TimeSpan DelayFactory(int executionIndex);
}