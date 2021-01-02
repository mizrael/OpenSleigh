using OpenSleigh.Core;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

//TODO: tests
[assembly: InternalsVisibleTo("OpenSleigh.Persistence.InMemory.Tests")]
namespace OpenSleigh.Persistence.InMemory
{
    internal class InMemoryPublisher : IPublisher 
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ITypesCache _typesCache;
        
        public InMemoryPublisher(IServiceProvider serviceProvider, ITypesCache typesCache)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _typesCache = typesCache ?? throw new ArgumentNullException(nameof(typesCache));
        }

        public async Task PublishAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var messageType = message.GetType();
            var rawWriterType = typeof(ChannelWriter<>);
            var writerType = _typesCache.GetGeneric(rawWriterType, messageType);
            var writer = _serviceProvider.GetService(writerType);
            if (null == writer)
                return;

            await (writer as dynamic).WriteAsync(message, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}