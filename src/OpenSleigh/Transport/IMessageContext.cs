namespace OpenSleigh.Transport;

public interface IMessageContext<out TM> where TM : IMessage
{
    TM Message { get; }
    string MessageId { get; }
    string CorrelationId { get; }
    string SenderId { get; }
}
