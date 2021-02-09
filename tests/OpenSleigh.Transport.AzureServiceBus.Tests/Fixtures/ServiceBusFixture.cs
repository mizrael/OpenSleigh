using Microsoft.Extensions.Configuration;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures
{
    public class ServiceBusFixture 
    {
        public ServiceBusFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("AzureServiceBus");
            this.Configuration = new AzureServiceBusConfiguration(connectionString);
        }

        public AzureServiceBusConfiguration Configuration { get; init; }
    }
}