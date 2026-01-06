using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;

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
            hostName: rabbitSection["HostName"],
            userName: rabbitSection["UserName"],
            password: rabbitSection["Password"],
            vhost: rabbitSection["VirtualHost"],
            retryDelay: System.TimeSpan.FromMilliseconds(retryDelayMs),
            durable: false,
            autoDelete: true);
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

    public QueueReferences CreateQueueReference()
    {
#if NET9_0_OR_GREATER
        var queueName = System.Guid.CreateVersion7().ToString("N");
#else
        var queueName = System.Guid.NewGuid().ToString("N");
#endif
        var result =  new QueueReferences(queueName, queueName, $"{queueName}.dead", $"{queueName}.dead");

        _queues.Add(result);

        return result;
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
