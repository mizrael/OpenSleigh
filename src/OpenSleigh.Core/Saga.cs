namespace OpenSleigh.Core
{
    public abstract class Saga<TD>
        where TD : SagaState
    {
        public TD State { get; internal set; }
        public IMessageBus Bus { get; internal set; }
    }
}