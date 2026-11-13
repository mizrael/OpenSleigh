using System.Reflection;
using Xunit.Sdk;

namespace OpenSleigh.E2ETests;

/// <summary>
/// marks the scenarios of an infrastructure whose hosts share nothing, such as the in-memory one:
/// a second host would have a store and a transport of its own and take no part in the scenario.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SingleHostAttribute : Attribute;

/// <summary>
/// the numbers of hosts a scenario runs with: one, a pair, and a few. Only one for a <see cref="SingleHostAttribute"/> class.
/// </summary>
public sealed class HostsCountDataAttribute : DataAttribute
{
    private static readonly int[] SharedInfrastructure = [1, 2, 5];
    private static readonly int[] IsolatedHosts = [1];

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        // the scenarios are declared on a base class: the class under test is the one the method was found through
        var testClass = testMethod.ReflectedType
            ?? throw new InvalidOperationException($"unable to tell which class '{testMethod.Name}' is run for.");

        var hostsCounts = testClass.IsDefined(typeof(SingleHostAttribute), inherit: true)
            ? IsolatedHosts
            : SharedInfrastructure;

        return hostsCounts.Select(hostsCount => new object[] { hostsCount });
    }
}
