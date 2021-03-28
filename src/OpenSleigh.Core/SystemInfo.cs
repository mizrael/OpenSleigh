using System;

namespace OpenSleigh.Core
{
    public class SystemInfo
    {
        public SystemInfo(Guid clientId, string clientGroup)
        {
            if (string.IsNullOrWhiteSpace(clientGroup))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(clientGroup));
            this.ClientId = clientId;
            this.ClientGroup = clientGroup;
        }
        
        public Guid ClientId { get; }
        public string ClientGroup { get; }
        public bool PublishOnly { get; internal set; }

        public static SystemInfo New()
        {
            var systemInfo = new SystemInfo(Guid.NewGuid(), System.AppDomain.CurrentDomain.FriendlyName);
            
            return systemInfo;
        }
    }
}
