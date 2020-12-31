namespace OpenSleigh.Core
{
    public abstract class Saga<TD>
        where TD : SagaState
    {
        protected void Publish<TM>(TM message) where TM : IMessage
        {
            this.State.AddToOutbox(message);
        }

        public TD State { get; internal set; }
    }
}