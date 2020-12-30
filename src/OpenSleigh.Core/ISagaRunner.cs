using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public interface ISagaRunner<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken)
            where TM : IMessage;
    }
}