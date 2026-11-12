using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.RabbitMQ;

[ExcludeFromCodeCoverage]
public record RabbitConfiguration
{
    public RabbitConfiguration(
        string hostName, 
        string userName, 
        string password,
        string? vhost = null,
        TimeSpan? retryDelay = null,
        bool durable = true,
        bool autoDelete = false)
    {
        HostName = hostName;
        UserName = userName;
        Password = password;
        RetryDelay = retryDelay ?? TimeSpan.FromSeconds(30);
        Durable = durable;
        AutoDelete = autoDelete;
        VirtualHost = string.IsNullOrWhiteSpace(vhost) ? "/" : vhost;
    }

    public string HostName { get; }
    public string VirtualHost { get; }
    public string UserName { get; }
    public string Password { get; }

    /// <summary>
    /// gets the delay for message re-enqueuing.
    /// </summary>
    public TimeSpan RetryDelay { get; }

    /// <summary>
    /// Gets whether exchanges and queues are durable. Defaults to true.
    /// </summary>
    public bool Durable { get; }

    /// <summary>
    /// Gets a value indicating whether the resource is automatically deleted when no longer in use. Defaults to false.
    /// </summary>
    public bool AutoDelete { get; }

    /// <summary>
    /// Gets how many messages a consumer handles at the same time. Defaults to 1: messages are handled one at a time,
    /// in the order the broker delivers them. Values above 1 raise throughput but give up that ordering, and messages
    /// for the same saga instance that arrive together will contend for its lock and go through the retry queue.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">the value is lower than 1: the consumer would handle nothing.</exception>
    public ushort ConsumerDispatchConcurrency
    {
        get => _consumerDispatchConcurrency;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinConsumerDispatchConcurrency);
            _consumerDispatchConcurrency = value;
        }
    }

    private const ushort MinConsumerDispatchConcurrency = 1;
    private readonly ushort _consumerDispatchConcurrency = MinConsumerDispatchConcurrency;

    /// <summary>
    /// Gets how many unacknowledged messages the broker hands to a consumer at once.
    /// Defaults to 0, which means no limit: the broker pushes messages as fast as it can.
    /// Set it when several workers share a queue, so one worker cannot hoard the backlog.
    /// </summary>
    public ushort PrefetchCount { get; init; }
}