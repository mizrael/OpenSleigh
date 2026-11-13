using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OpenSleigh.E2ETests.Harness.Transports;

/// <summary>
/// an exchange per message type and a queue per service, all named after the run, like the default topology
/// of the transport. Nothing is durable.
/// </summary>
internal sealed class RabbitMqTransport : ITransport
{
    private const string SettingsSection = "Rabbit";
    private const int DefaultRetryDelayInMilliseconds = 500;

    private readonly string _runId;
    private readonly RabbitConfiguration _configuration;
    private readonly ConcurrentDictionary<QueueReferences, Type> _queues = new();

    public RabbitMqTransport(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _runId = context.RunId;

        var settings = context.Configuration.GetSection(SettingsSection);
        var retryDelay = settings.GetValue("RetryDelayInMilliseconds", DefaultRetryDelayInMilliseconds);

        _configuration = new RabbitConfiguration(
            hostName: settings["HostName"] ?? throw new InvalidOperationException("the RabbitMQ host name is not configured."),
            userName: settings["UserName"]!,
            password: settings["Password"]!,
            vhost: settings["VirtualHost"],
            retryDelay: TimeSpan.FromMilliseconds(retryDelay),
            durable: false,
            autoDelete: true);
    }

    public void Configure(IBusConfigurator bus, string service)
        => bus.UseRabbitMQTransport(_configuration, messageType => CreateQueueReferences(messageType, service));

    private QueueReferences CreateQueueReferences(Type messageType, string service)
    {
        var exchangeName = $"{_runId}.{messageType.Name}";
        var deadLetterExchangeName = $"{exchangeName}.dead";

        return new QueueReferences(
            exchangeName,
            queueName: $"{exchangeName}.{service}",
            routingKey: exchangeName,
            deadLetterExchangeName,
            deadLetterQueue: $"{deadLetterExchangeName}.{service}");
    }

    public Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
    {
        var service = hostServices.GetServiceName();
        foreach (var messageType in hostServices.GetHandledMessageTypes())
            _queues.TryAdd(CreateQueueReferences(messageType, service), messageType);

        return Task.CompletedTask;
    }

    public bool SupportsDeadLetters => true;

    public async Task<IReadOnlyDictionary<Type, long>> ReadDeadLettersAsync(CancellationToken cancellationToken)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken);

        // every service has a dead letter queue of its own for a message type
        var deadLetters = new Dictionary<Type, long>();
        foreach (var (queue, messageType) in _queues)
        {
            var count = await CountMessagesAsync(connection, queue.DeadLetterQueue, cancellationToken);
            if (count > 0)
                deadLetters[messageType] = deadLetters.GetValueOrDefault(messageType) + count;
        }

        return deadLetters;
    }

    // a channel is closed by the broker when it asks for a queue that does not exist: one channel per question
    private static async Task<uint> CountMessagesAsync(IConnection connection, string queueName, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        try
        {
            var queue = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
            return queue.MessageCount;
        }
        catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == Constants.NotFound)
        {
            return 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_queues.IsEmpty)
            return;

        // dead letter and retry queues never have a consumer, so the broker never deletes them by itself
        await using var connection = await CreateConnectionAsync(CancellationToken.None);
        foreach (var queue in _queues.Keys)
            await TryDeleteAsync(connection, queue);
    }

    private static async Task TryDeleteAsync(IConnection connection, QueueReferences queue)
    {
        try
        {
            await using var channel = await connection.CreateChannelAsync();
            await channel.DeleteAsync(queue);
        }
        catch (Exception e) when (e is OperationInterruptedException or AlreadyClosedException)
        {
            // what is left behind does not outlive the broker of the test run
        }
    }

    private Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
        => new ConnectionFactory
        {
            HostName = _configuration.HostName,
            UserName = _configuration.UserName,
            Password = _configuration.Password,
            VirtualHost = _configuration.VirtualHost,
            Port = AmqpTcpEndpoint.UseDefaultPort,
        }.CreateConnectionAsync(cancellationToken);
}
