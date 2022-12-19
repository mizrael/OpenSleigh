using OpenSleigh.Transport;

namespace OpenSleigh.Tests
{
    internal class FakeMessageContext<TM> : IMessageContext<TM> where TM : IMessage
    {
        public static FakeMessageContext<TM> Create(TM message, string? correlationId = null, string? parentId = null, string? senderId = null)
            => new FakeMessageContext<TM>(){
                Id = Guid.NewGuid().ToString(),
                ParentId = parentId,
                SenderId = senderId,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
                Message = message,
            };

        public required TM Message { get; init; }

        public required string Id { get; init; }

        public required string CorrelationId { get; init; }

        public string? ParentId { get; init; }

        public string? SenderId { get; init; }
    }
}