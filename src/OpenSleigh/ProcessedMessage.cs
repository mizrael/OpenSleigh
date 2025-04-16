using OpenSleigh.Transport;

namespace OpenSleigh;

public record ProcessedMessage
{
    public required string IdempotencyKey { get; init; }
    public required string MessageId { get; init; }
    public required DateTimeOffset When { get; init; }

    public override int GetHashCode()
        => this.IdempotencyKey.GetHashCode();

    public static ProcessedMessage Create<TM>(IMessageContext<TM> messageContext) where TM : IMessage
        => new ProcessedMessage()
        {
            IdempotencyKey = messageContext.IdempotencyKey,
            MessageId = messageContext.Id,
            When = DateTimeOffset.UtcNow
        };
}