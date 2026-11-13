using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// what the harness tells a host about itself. Registered in the services of every host.
/// </summary>
/// <param name="Service">the service the host is an instance of.</param>
public sealed record HostIdentity(string Service)
{
    public const string DefaultService = "workers";
}

internal static class HostServicesExtensions
{
    /// <summary>the message types the sagas of a host handle.</summary>
    public static IReadOnlyCollection<Type> GetHandledMessageTypes(this IServiceProvider hostServices)
        => hostServices.GetRequiredService<ISagaDescriptorsResolver>().GetRegisteredMessageTypes().ToArray();

    public static string GetServiceName(this IServiceProvider hostServices)
        => hostServices.GetRequiredService<HostIdentity>().Service;
}
