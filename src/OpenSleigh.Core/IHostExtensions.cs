using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.Core
{
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