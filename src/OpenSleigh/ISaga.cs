namespace OpenSleigh
{
    public interface ISaga 
    {
        ISagaExecutionContext Context { get; }
    }

    public interface ISaga<TS> : ISaga
        where TS : new()
    {
        new ISagaExecutionContext<TS> Context { get; }
    }
}