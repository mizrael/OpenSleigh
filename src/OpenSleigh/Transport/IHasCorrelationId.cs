namespace OpenSleigh.Transport;

public interface IHasCorrelationId
{
    string CorrelationId { get; }
}