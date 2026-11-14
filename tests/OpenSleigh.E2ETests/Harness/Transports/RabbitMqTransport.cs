using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OpenSleigh.E2ETests.Harness.Transports;

/// <summary>
/// an exchange per message type and a queue per service, all named after the run, like the default topology
/// of the transport. Nothing is durable, and nothing deletes itself: a queue that went away with the last host
/// of its service would take its messages with it, and the host that replaces it would not declare it again.
/// </summary>
internal sealed class RabbitMqTransport : ITransport
{
    private const string SettingsSection = "Rabbit";
    private const int DefaultRetryDelayInMilliseconds = 500;
    private static readonly TimeSpan SubscriptionPollingInterval = TimeSpan.FromMilliseconds(20);

    private readonly string _runId;
    private readonly RabbitConfiguration _configuration;

    // everything a host of this run asked the references of, which is everything it can have declared:
    // a host declares queues for the types it handles, and for its own service when it publishes a type
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
            autoDelete: false);
    }

    public void Configure(IBusConfigurator bus, string service)
        => bus.UseRabbitMQTransport(_configuration, messageType =>
        {
            var references = CreateQueueReferences(messageType, service);
            _queues.TryAdd(references, messageType);
            return references;
        });

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

    public Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken) => Task.CompletedTask;

    // a message published to an exchange before the queue of a service is bound to it never reaches that service,
    // and the hosts of a service share its queues: each of them has to be consuming
    public async Task WaitUntilSubscribedAsync(IReadOnlyCollection<IServiceProvider> runningHosts, CancellationToken cancellationToken)
    {
        var expectedConsumers = runningHosts
            .Where(host => host.Consumes())
            .SelectMany(host => host.GetHandledMessageTypes()
                .Select(messageType => CreateQueueReferences(messageType, host.GetServiceName()).QueueName))
            .GroupBy(queueName => queueName)
            .ToDictionary(queue => queue.Key, queue => queue.Count());

        if (expectedConsumers.Count == 0)
            return;

        await using var connection = await CreateConnectionAsync(cancellationToken);
        foreach (var (queueName, expected) in expectedConsumers)
        {
            while (((await FindQueueAsync(connection, queueName, cancellationToken))?.ConsumerCount ?? 0) < expected)
                await Task.Delay(SubscriptionPollingInterval, cancellationToken);
        }
    }

    public bool SupportsDeadLetters => true;

    public async Task<IReadOnlyDictionary<Type, long>> ReadDeadLettersAsync(CancellationToken cancellationToken)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken);

        // every service has a dead letter queue of its own for a message type
        var deadLetters = new Dictionary<Type, long>();
        foreach (var (queue, messageType) in _queues)
        {
            var count = (await FindQueueAsync(connection, queue.DeadLetterQueue, cancellationToken))?.MessageCount ?? 0;
            if (count > 0)
                deadLetters[messageType] = deadLetters.GetValueOrDefault(messageType) + count;
        }

        return deadLetters;
    }

    // a channel is closed by the broker when it asks for a queue that does not exist: one channel per question
    private static async Task<QueueDeclareOk?> FindQueueAsync(IConnection connection, string queueName, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        try
        {
            return await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
        }
        catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == Constants.NotFound)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_queues.IsEmpty)
            return;

        // deleting what was never declared is not an error for the broker
        await using var connection = await CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();
        foreach (var queue in _queues.Keys)
            await channel.DeleteAsync(queue);
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
