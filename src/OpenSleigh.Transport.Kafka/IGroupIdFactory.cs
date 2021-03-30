using System;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.Kafka
{
    public interface IGroupIdFactory
    {
        string Create<TM>() where TM : IMessage;
    }

    internal class DefaultGroupIdFactory : IGroupIdFactory
    {
        private readonly SystemInfo _systemInfo;

        public DefaultGroupIdFactory(SystemInfo systemInfo)
        {
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        public string Create<TM>() where TM : IMessage
        {
            var messageType = typeof(TM);

            var groupId = messageType.IsEvent()
                ? $"{messageType.FullName}.{_systemInfo.ClientGroup}"
                : messageType.FullName;
            return groupId;
        }
    }
}