using System;

namespace OpenSleigh.Core
{
    public interface ISystemInfo
    {
        string ClientGroup { get; }
        Guid ClientId { get; }
        bool PublishOnly { get; }
    }
}
