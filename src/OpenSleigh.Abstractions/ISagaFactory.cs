namespace OpenSleigh.Core
{
    public interface ISagaFactory<out TS, in TD>
        where TD : SagaState
        where TS : Saga<TD>
    {
        TS Create(TD state);
    }
}