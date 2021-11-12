using Microsoft.Extensions.Configuration;
using NSubstitute;
using RabbitMQ.Client;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Fixtures
{
    public class RabbitFixture : IAsyncLifetime
    {
        private readonly List<string> _queues = new();

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

        /// <summary>
        /// returns a RabbitMQ connection. Needs to be disposed after use.
        /// </summary>
        public IConnection Connect()
        {
            var connectionFactory = new ConnectionFactory()
            {
                HostName = RabbitConfiguration.HostName,
                UserName = RabbitConfiguration.UserName,
                Password = RabbitConfiguration.Password,
                Port = AmqpTcpEndpoint.UseDefaultPort,
                DispatchConsumersAsync = true
            };
            return connectionFactory.CreateConnection();
        }

        public QueueReferences CreateQueueReference(string queueName)
        {
            _queues.Add(queueName);
            return new QueueReferences(queueName, queueName, $"{queueName}.dead", $"{queueName}.dead");
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (!_queues.Any())
                return;

            using var connection = Connect();
            using var channel = connection.CreateModel();
            
            foreach (var queueName in _queues) {
                channel.ExchangeDelete(queueName);
                channel.QueueDelete(queueName);

                channel.ExchangeDelete(queueName + ".dead");
                channel.QueueDelete(queueName + ".dead");

                channel.ExchangeDelete(queueName + ".retry");
                channel.QueueDelete(queueName + ".retry");
            }
        }

        public RabbitConfiguration RabbitConfiguration { get; init; }
    }
}