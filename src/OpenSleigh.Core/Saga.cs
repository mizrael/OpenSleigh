using System;
using System.Runtime.CompilerServices;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("OpenSleigh.Core.Tests")]
namespace OpenSleigh.Core
{
    //TODO: add possibility to mark as complete
    public abstract class Saga<TD>
        where TD : SagaState
    {
        public TD State { get; private set; }
        public IMessageBus Bus { get; private set; }

        internal void SetState(TD state)
        {
            this.State = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal void SetBus(IMessageBus bus)
        {
            this.Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }
    }
}