using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities
{
    public record OutboxMessage
    {
        public enum Statuses
        {
            Pending = 0,
            Processed
        }

        public Statuses Status { get; set; }

        public string? LockId { get; set; }
        public DateTimeOffset? LockTime { get; set; }
        
        public required string CorrelationId { get; set; }
        public required byte[] Body { get; set; }
        public required string MessageId { get; set; }
        public required Type MessageType { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
        public string? ParentId { get; set; }
        public string? SenderId { get; set; }

        public Outbox.OutboxMessage ToModel()
            => new Outbox.OutboxMessage()
            {
                Body = Body,
                MessageId = MessageId,
                CorrelationId = CorrelationId,
                CreatedAt = CreatedAt,
                MessageType = MessageType,
                ParentId = ParentId,
                SenderId = SenderId
            };

        public static OutboxMessage Create(Outbox.OutboxMessage message)
            => new OutboxMessage()
            {
                Body = message.Body.ToArray(),
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId,
                CreatedAt = message.CreatedAt,
                MessageType = message.MessageType,
                ParentId = message.ParentId,
                SenderId = message.SenderId,
                Status = Statuses.Pending,
            };
    }
    
    internal class OutboxMessageStateEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("OutboxMessages", Constants.DbSchema);

            builder.HasKey(e => e.MessageId);
        }
    }
}