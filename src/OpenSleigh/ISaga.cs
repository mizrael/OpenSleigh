namespace OpenSleigh;

public interface ISaga
{
    ISagaInstance Context { get; }
}

public interface ISaga<TS> : ISaga
    where TS : new()
{
    new ISagaInstance<TS> Context { get; }
}