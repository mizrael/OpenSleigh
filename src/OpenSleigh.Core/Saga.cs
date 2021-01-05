using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenSleigh.Core.Tests")]
namespace OpenSleigh.Core
{
    //TODO: add possibility to mark as complete
    public abstract class Saga<TD>
        where TD : SagaState
    {
        public TD State { get; internal set; }
        public IMessageBus Bus { get; internal set; }
    }
}