using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using System;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal class AzureServiceBusInfrastructureCreator<TM> : IInfrastructureCreator
        where TM : IMessage
    {
        public async Task SetupAsync(IHost host)
        {
            using var scope = host.Services.CreateScope();

            var referenceFactory = scope.ServiceProvider.GetRequiredService<IQueueReferenceFactory>();
            var policy = referenceFactory.Create<TM>();

            var azureConfig = scope.ServiceProvider.GetRequiredService<AzureServiceBusConfiguration>();
            var adminClient = new ServiceBusAdministrationClient(azureConfig.ConnectionString);

            try
            {
                if (!await adminClient.TopicExistsAsync(policy.TopicName))
                    await adminClient.CreateTopicAsync(new CreateTopicOptions(policy.TopicName)
                    {
                        RequiresDuplicateDetection = true
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            try
            {
                if (!await adminClient.SubscriptionExistsAsync(policy.TopicName, policy.SubscriptionName))
                    await adminClient.CreateSubscriptionAsync(policy.TopicName, policy.SubscriptionName);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
           
        }
    }
}