using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// what makes a host of a scenario: its sagas, its role, the services its sagas need.
/// </summary>
/// <param name="ConfigureBus">registers the sagas, and whatever else the bus of this host needs,
/// such as <see cref="IBusConfigurator.SetPublishOnly"/>. The store and the transport are configured already.</param>
public sealed record HostSetup(Action<IBusConfigurator> ConfigureBus)
{
    /// <summary>
    /// runs after everything else is registered: the place for the callbacks the sagas report to,
    /// and for decorators that replace a service of the library with one that fails on purpose.
    /// </summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>
    /// the service this host is an instance of. Hosts of one service compete for its messages.
    /// </summary>
    public string Service { get; init; } = HostIdentity.DefaultService;
}
