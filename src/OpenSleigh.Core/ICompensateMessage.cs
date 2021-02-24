using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ICompensateMessage<in TM> where TM : IMessage
    {
        Task CompensateAsync(IMessageContext<TM> context, CancellationToken cancellationToken = default);
    }
}