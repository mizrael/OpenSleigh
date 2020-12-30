using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public interface IHandleMessage<in TM> where TM : IMessage
    {
        Task HandleAsync(IMessageContext<TM> context, CancellationToken cancellationToken = default);
    }
}