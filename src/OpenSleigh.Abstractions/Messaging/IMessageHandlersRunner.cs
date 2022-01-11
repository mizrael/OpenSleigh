using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Messaging
{
    public interface IMessageHandlersRunner
    {
        Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage;
    }
}