using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.DependencyInjection
{
    public interface IMessageHandlerConfigurator<TM> where TM : IMessage {
        IServiceCollection Services { get; }
    }

    [ExcludeFromCodeCoverage]
    internal class MessageHandlerConfigurator<TM> : IMessageHandlerConfigurator<TM> 
        where TM : IMessage {
        public MessageHandlerConfigurator(IServiceCollection services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public IServiceCollection Services { get; }
    }
}