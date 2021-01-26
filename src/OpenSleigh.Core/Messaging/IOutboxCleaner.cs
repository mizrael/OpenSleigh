using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Messaging
{
    public interface IOutboxCleaner
    {
        Task RunCleanupAsync(CancellationToken cancellationToken = default);
    }
}