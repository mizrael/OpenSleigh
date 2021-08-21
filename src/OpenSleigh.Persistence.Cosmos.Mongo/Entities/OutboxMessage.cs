using System;

namespace OpenSleigh.Persistence.Cosmos.Mongo.Entities
{
    public record OutboxMessage(Guid Id, ReadOnlyMemory<byte> Data, string Type, string Status,
        DateTime? PublishingDate = null, 
        Guid? LockId = null, DateTime? LockTime = null);
}