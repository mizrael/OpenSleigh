using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Messaging
{
    public interface IOutboxProcessor
    {
        Task StartAsync(CancellationToken cancellationToken = default);
    }
}