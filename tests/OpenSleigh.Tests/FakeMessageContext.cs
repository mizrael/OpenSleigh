using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

internal class FakeMessageContext<TM> : IMessageContext<TM> where TM : IMessage
{
    public static FakeMessageContext<TM> Create(
        TM message, 
        string? messageId = null,
        string? correlationId = null, 
        string? parentId = null, 
        string? senderId = null)
        => new FakeMessageContext<TM>(){
            MessageId = messageId ?? Guid.NewGuid().ToString(),
            ParentId = parentId,
            SenderId = senderId ?? Guid.NewGuid().ToString(),
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            Message = message,
        };

    public required TM Message { get; init; }

    public required string MessageId { get; init; }

    public required string CorrelationId { get; init; }
    public required string SenderId { get; init; }

    public string? ParentId { get; init; }
}