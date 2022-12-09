namespace OpenSleigh.Messaging
{
    public interface IMessageContext<out TM> where TM : IMessage
    {
        TM Message { get; }
        string Id { get; }
        string CorrelationId { get; }
        string? ParentId { get; }
        string? SenderId { get; }
    }
}