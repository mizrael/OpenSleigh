using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities
{
    public class SagaState
    {
        private SagaState() { }

        public SagaState(Guid correlationId, string type, byte[] data, Guid? lockId = null, DateTime? lockTime = null)
        {
            CorrelationId = correlationId;
            Type = type;
            Data = data;
            LockId = lockId;
            LockTime = lockTime;
        }

        public Guid CorrelationId { get; }
        public string Type { get; }
        public byte[] Data { get; set; }
        public Guid? LockId { get; set; }
        public DateTime? LockTime { get; set; }
    }

    internal class SagaStateEntityTypeConfiguration : IEntityTypeConfiguration<SagaState>
    {
        public void Configure(EntityTypeBuilder<SagaState> builder)
        {
            builder.ToTable("SagaStates", "dbo");

            builder.HasKey(e => new {e.CorrelationId, e.Type});
        }
    }

}
