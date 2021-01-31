using System;
using System.Reflection;

namespace OpenSleigh.Core
{
    public class SystemInfo
    {
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public string ClientGroup { get; set; }
        public bool PublishOnly { get; set; } = false;

        public static SystemInfo New()
        {
            var systemInfo = new SystemInfo()
            {
                ClientGroup = System.AppDomain.CurrentDomain.FriendlyName
            };
            return systemInfo;
        }
    }
}
