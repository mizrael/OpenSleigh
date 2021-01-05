using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public interface IOutboxProcessor
    {
        Task StartAsync(CancellationToken cancellationToken = default);
    }
}