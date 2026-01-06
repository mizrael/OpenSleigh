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
}