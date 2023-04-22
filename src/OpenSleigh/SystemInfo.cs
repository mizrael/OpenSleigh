using Microsoft.Extensions.Configuration;

namespace OpenSleigh
{
    internal record SystemInfo : ISystemInfo
    {
        public SystemInfo(string clientGroup, string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException($"'{nameof(clientId)}' cannot be null or whitespace.", nameof(clientId));
            
            if (string.IsNullOrWhiteSpace(clientGroup))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(clientGroup));

            ClientId = clientId;
            ClientGroup = clientGroup;
        }

        public string ClientId { get; }
        public string ClientGroup { get; }
        public bool PublishOnly { get; internal set; } = false;

        internal static SystemInfo Create(IConfigurationRoot? configuration = null)
        {
            string? clientId = null;
            string? clientGroup = null;
            if (configuration is not null)
            {
                var section = configuration.GetSection("OpenSleigh");
                clientGroup = section != null ? section["ClientGroup"] : null;
                clientId = section != null ? section["ClientId"] : null;
            }

            if (string.IsNullOrWhiteSpace(clientGroup))
                clientGroup = AppDomain.CurrentDomain?.FriendlyName;
            if (string.IsNullOrWhiteSpace(clientId))
                clientId = Guid.NewGuid().ToString();

            return new SystemInfo(clientGroup: clientGroup, clientId: clientId);
        }
    }
}