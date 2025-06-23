namespace OpenSleigh.Transport;

public interface IHasCorrelationId
{
    string CorrelationId { get; }
}

public interface IHasRequestId
{
    string RequestId { get; }
}