using OpenSleigh.Messaging;

namespace OpenSleigh
{
    public record ProcessedMessage
    {
        public required string MessageId { get; init; }
        public required DateTimeOffset When { get; init; }

        public override int GetHashCode()
            => this.MessageId.GetHashCode();

        public static ProcessedMessage Create<TM>(IMessageContext<TM> messageContext) where TM : IMessage
            => new ProcessedMessage()
            {
                MessageId = messageContext.Id,
                When = DateTimeOffset.UtcNow
            };
    }
}