namespace OpenSleigh.Core
{
    public interface ISagaFactory<TS, TD>
        where TD : SagaState
        where TS : Saga<TD>
    {
        TS Create(TD state);
    }
}