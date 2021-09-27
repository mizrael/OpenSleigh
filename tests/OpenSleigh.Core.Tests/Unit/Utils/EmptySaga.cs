namespace OpenSleigh.Core.Tests.Unit.Utils
{
    internal class EmptySaga : Saga<SagaState>
    {
        public EmptySaga(SagaState state) : base(state)
        {
        }
    }
}
