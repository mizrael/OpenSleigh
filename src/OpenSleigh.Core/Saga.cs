using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("OpenSleigh.Core.Tests")]
namespace OpenSleigh.Core
{
    public abstract class Saga<TD> : ISaga
        where TD : SagaState
    {
        public TD State { get; private set; }
        
        internal void SetState(TD state)
        {
            this.State = state ?? throw new ArgumentNullException(nameof(state));
        }

        protected void Publish<TM>(TM message) where TM : IMessage
            => this.State.AddToOutbox(message);
    }
}