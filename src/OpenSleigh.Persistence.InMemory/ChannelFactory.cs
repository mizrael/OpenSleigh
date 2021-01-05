using System;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Persistence.InMemory
{
    internal class ChannelFactory : IChannelFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public ChannelFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public ChannelWriter<TM> GetWriter<TM>() 
            where TM : IMessage
            => _serviceProvider.GetService<ChannelWriter<TM>>();
    }
}