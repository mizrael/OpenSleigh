using System;

namespace OpenSleigh.Persistence.Mongo.Entities
{
    public record OutboxMessage(Guid Id, byte[] Data, string Type, string Status, DateTime? PublishingDate = null);
}