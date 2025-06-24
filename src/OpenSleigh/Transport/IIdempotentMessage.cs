namespace OpenSleigh.Transport;

public interface IIdempotentMessage : IMessage, IHasRequestId
{
    IEnumerable<object> GetIdempotencyComponents();
}
