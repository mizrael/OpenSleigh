using Microsoft.Extensions.Hosting;

namespace OpenSleigh
{
    public interface IInfrastructureCreator
    {
        Task SetupAsync(IHost host);
    }
}