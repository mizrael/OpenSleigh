using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.RabbitMQ
{
    [ExcludeFromCodeCoverage]
    public record RabbitConfiguration
    {
        public RabbitConfiguration(string hostName, string userName, string password)
            : this(hostName, userName, password, TimeSpan.FromSeconds(30)) { }

        public RabbitConfiguration(string hostName, string userName, string password, TimeSpan retryDelay)
            : this(hostName: hostName, vhost: null, userName: userName, password:password, retryDelay) { }

        public RabbitConfiguration(string hostName, string vhost, string userName, string password, TimeSpan retryDelay)
        {
            HostName = hostName;
            UserName = userName;
            Password = password;
            RetryDelay = retryDelay;

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
    }
}