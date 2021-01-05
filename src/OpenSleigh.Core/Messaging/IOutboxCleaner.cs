using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Messaging
{
    public interface IOutboxCleaner
    {
        Task StartAsync(CancellationToken cancellationToken = default);
    }
}