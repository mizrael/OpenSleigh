using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures
{
    public class ServiceBusFixture : IDisposable
    {
        public ServiceBusFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddUserSecrets<ServiceBusFixture>()
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("AzureServiceBus");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("missing Service Bus connection");
            
            this.Configuration = new AzureServiceBusConfiguration(connectionString);
        }

        public AzureServiceBusConfiguration Configuration { get; init; }

        public void Dispose()
        {
            // uncomment this in case too many topics remain hanging

            //var adminClient = new ServiceBusAdministrationClient(this.Configuration.ConnectionString);
            //var t = Task.Run(async () =>
            //{
            //    var topics = adminClient.GetTopicsAsync();
            //    await foreach (var topic in topics)
            //        await adminClient.DeleteTopicAsync(topic.Name);
            //});
            //t.Wait();
        }
    }
}