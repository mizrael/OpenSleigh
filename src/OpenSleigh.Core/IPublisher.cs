using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public interface IPublisher
    {
        Task PublishAsync(IMessage message, CancellationToken cancellationToken = default);
    }
}