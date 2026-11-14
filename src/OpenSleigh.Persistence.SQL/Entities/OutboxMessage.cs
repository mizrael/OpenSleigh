using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL.Entities;

public record OutboxMessage
{
    public const string TableName = "OutboxMessages";
    public const string DeliverAtIndexName = "IX_OutboxMessages_DeliverAt";

    public required string CorrelationId { get; set; }
    public required byte[] Body { get; set; }
    public required string MessageId { get; set; }
    public required string MessageType { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string? ParentId { get; set; }
    public required string SenderId { get; set; }

    /// <summary>
    /// the earliest time, in UTC, at which the relay publishes the message. Null when there is no delay,
    /// which is also what a row written by a version of the library that does not know the column has.
    /// </summary>
    public DateTimeOffset? DeliverAt { get; set; }

    public bool TryMap(
        ITypeResolver typeResolver, 
        ISerializer serializer,
        [NotNullWhen(true)] out Outbox.MessageEnvelope? envelope)
    {
        ArgumentNullException.ThrowIfNull(typeResolver, nameof(typeResolver));

        var type = typeResolver.Resolve(MessageType, false);
        if(type is null)
        {
            envelope = null;
            return false;
        }

        return Outbox.MessageEnvelope.TryCreate(
            Body,
            messageId: MessageId,
            correlationId: CorrelationId,
            CreatedAt,
            type,
            senderId: SenderId,
            deliverAt: DeliverAt,
            serializer,
            out envelope);
    }

    public static OutboxMessage Map(Outbox.MessageEnvelope message, ISerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(serializer);

        var body = serializer.Serialize(message.Message);

        return new OutboxMessage()
        {
            Body = body,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            CreatedAt = message.CreatedAt,
            MessageType = message.MessageType.FullName,
            SenderId = message.SenderId,
            DeliverAt = message.DeliverAt,
        };
    }
}

internal class OutboxMessageStateEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(OutboxMessage.TableName, Constants.DbSchema);

        builder.HasKey(e => e.MessageId);

        // the relay looks up the delayed messages that are due, and the ones without a delay, through this index:
        // the ones that are waiting for their time are never read
        builder.HasIndex(e => e.DeliverAt)
            .HasDatabaseName(OutboxMessage.DeliverAtIndexName);
    }
}