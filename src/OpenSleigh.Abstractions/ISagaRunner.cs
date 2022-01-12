using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISagaRunner
    {
        Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage;
    }
    
    public interface ISagaRunner<TS, TD> : ISagaRunner
        where TS : Saga<TD>
        where TD : SagaState
    {
    }
}