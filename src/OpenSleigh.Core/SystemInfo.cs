using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenSleigh.Core.Tests")]
namespace OpenSleigh.Core
{ 
    internal record SystemInfo : ISystemInfo
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
        public bool PublishOnly { get; internal set; } = false;

        internal static SystemInfo New()
            => new SystemInfo(Guid.NewGuid(), AppDomain.CurrentDomain.FriendlyName);
    }
}
