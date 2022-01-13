using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures
{
    public class ServiceBusFixture : IAsyncLifetime 
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
            
            this.Configuration = new AzureServiceBusConfiguration(connectionString, 5);
        }

        public AzureServiceBusConfiguration Configuration { get; init; }

        public Task InitializeAsync()
            => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            // uncomment this in case too many topics remain hanging

            //var adminClient = new Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient(this.Configuration.ConnectionString);            
            //await foreach (var topic in adminClient.GetTopicsAsync())
            //    await adminClient.DeleteTopicAsync(topic.Name);
        }
    }
}