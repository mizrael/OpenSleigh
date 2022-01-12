using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core.Messaging
{
    public interface IMessageBus
    {
        Task PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage;
    }
}