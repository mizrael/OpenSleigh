using System;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    internal class KafkaInfrastructureCreator<TM> : IInfrastructureCreator
        where TM : IMessage
    {        
        private readonly ILogger<KafkaInfrastructureCreator<TM>> _logger;

        public KafkaInfrastructureCreator(ILogger<KafkaInfrastructureCreator<TM>> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SetupAsync(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var builder = scope.ServiceProvider.GetRequiredService<AdminClientBuilder>();
            var queueReferenceFactory = scope.ServiceProvider.GetRequiredService<IQueueReferenceFactory>();
            var queueRef = queueReferenceFactory.Create<TM>();

            using var adminClient = builder.Build();

            _logger.LogInformation("Setting up Kafka topic {Topic} ...", queueRef.TopicName);

            try
            {
                await adminClient.CreateTopicsAsync(new[] {
                    new TopicSpecification { Name = queueRef.TopicName, ReplicationFactor = 1, NumPartitions = 1 },
                    new TopicSpecification { Name = queueRef.DeadLetterTopicName, ReplicationFactor = 1, NumPartitions = 1 }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An error occured creating topic {Topic}: {Error}",
                    queueRef.TopicName, e.Message);
            }            
        }
    }
}