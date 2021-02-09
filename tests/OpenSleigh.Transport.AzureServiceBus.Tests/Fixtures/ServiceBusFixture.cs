using System;
using Microsoft.Extensions.Configuration;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures
{
    public class ServiceBusFixture 
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
    }
}