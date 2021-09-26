using System;
using System.Runtime.CompilerServices;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("OpenSleigh.Core.Tests")]
namespace OpenSleigh.Core
{
    public abstract class Saga<TD> : ISaga
        where TD : SagaState
    {
        public TD State { get; }

        protected Saga(TD state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        protected void Publish<TM>(TM message) where TM : IMessage
            => this.State.AddToOutbox(message);
    }
}