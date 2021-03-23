using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    public interface IKafkaPublisherExecutor
    {
        Task<DeliveryResult<Guid, byte[]>> PublishAsync(IMessage message, 
            string topic,
            IEnumerable<Header> additionalHeaders = null,
            CancellationToken cancellationToken = default);
    }
}