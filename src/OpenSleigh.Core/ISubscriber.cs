using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISubscriber
    {
        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync(CancellationToken cancellationToken = default);
    }

    public interface ISubscriber<TM> : ISubscriber
        where TM : IMessage
    { }
}