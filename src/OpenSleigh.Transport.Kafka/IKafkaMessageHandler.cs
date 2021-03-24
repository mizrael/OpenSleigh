using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka
{
    public interface IKafkaMessageHandler
    {
        Task HandleAsync(ConsumeResult<Guid, byte[]> result, QueueReferences queueReferences, CancellationToken cancellationToken = default);
    }
}