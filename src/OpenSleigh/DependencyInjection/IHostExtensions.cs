using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    public static class IHostExtensions
    {
        public static async Task SetupInfrastructureAsync(this IHost host)
        {
            using var scope = host.Services.CreateScope();
            var creators = scope.ServiceProvider.GetServices<IInfrastructureCreator>();
            foreach (var creator in creators)
                await creator.SetupAsync(host);
        }
    }
}