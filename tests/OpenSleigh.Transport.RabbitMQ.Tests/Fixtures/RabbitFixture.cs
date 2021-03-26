using Microsoft.Extensions.Configuration;
using System;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Fixtures
{
    public class RabbitFixture
    {
        public RabbitFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var rabbitSection = configuration.GetSection("Rabbit");
            this.RabbitConfiguration = new RabbitConfiguration(rabbitSection["HostName"],
                rabbitSection["UserName"],
                rabbitSection["Password"]);
        }

        public RabbitConfiguration RabbitConfiguration { get; init; }
    }
}