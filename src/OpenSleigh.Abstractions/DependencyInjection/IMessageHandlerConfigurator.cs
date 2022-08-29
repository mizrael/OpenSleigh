using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.DependencyInjection
{
    public interface IMessageHandlerConfigurator<TM> where TM : IMessage {
        IServiceCollection Services { get; }
    }
}