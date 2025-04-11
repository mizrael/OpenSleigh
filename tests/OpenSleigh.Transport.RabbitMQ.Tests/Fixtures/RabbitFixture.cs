using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Fixtures
{
    public class RabbitFixture : IAsyncLifetime
    {
        private readonly List<QueueReferences> _queues = new();

        public RabbitFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var rabbitSection = configuration.GetSection("Rabbit");

            if (!int.TryParse(rabbitSection["RetryDelayInMilliseconds"], out var retryDelayMs))
                retryDelayMs = 1000;

            this.RabbitConfiguration = new RabbitConfiguration(
                rabbitSection["HostName"],
                rabbitSection["VirtualHost"],
                rabbitSection["UserName"],
                rabbitSection["Password"],
                System.TimeSpan.FromMilliseconds(retryDelayMs));
        }

        private ConnectionFactory CreateConnectionFactory()
        => new ConnectionFactory()
        {
            HostName = RabbitConfiguration.HostName,
            UserName = RabbitConfiguration.UserName,
            Password = RabbitConfiguration.Password,
            VirtualHost = RabbitConfiguration.VirtualHost,
            Port = AmqpTcpEndpoint.UseDefaultPort,
        };

        private QueueReferences CreateQueueReference()
        {
            var queueName = System.Guid.CreateVersion7().ToString("N");
            return new QueueReferences(queueName, queueName, $"{queueName}.dead", $"{queueName}.dead");
        }

        public async ValueTask<QueueReferences> CreateQueueReferenceAsync(IChannel channel)
        {
            var queueRef = this.CreateQueueReference();
            _queues.Add(queueRef);

            await channel.ExchangeDeclareAsync(queueRef.ExchangeName, ExchangeType.Topic, false, true);
            await channel.QueueDeclareAsync(queue: queueRef.QueueName,
                durable: false,
                exclusive: false,
                autoDelete: true,
                arguments: null);
            await channel.QueueBindAsync(queueRef.QueueName,
                              queueRef.ExchangeName,
                              routingKey: queueRef.RoutingKey,
                              arguments: null);

            return queueRef;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (!_queues.Any())
                return;

            using var connection = await this.ConnectionFactory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();
            
            foreach (var queueRef in _queues) 
            {
                await channel.DeleteAsync(queueRef);
            }
        }

        public RabbitConfiguration RabbitConfiguration { get; }
        public ConnectionFactory ConnectionFactory => CreateConnectionFactory();
    }
}
