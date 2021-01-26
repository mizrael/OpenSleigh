using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("OpenSleigh.Persistence.InMemory.Tests")]
namespace OpenSleigh.Persistence.InMemory.Messaging
{
    internal class InMemoryPublisher : IPublisher 
    {
        private readonly IChannelFactory _channelFactory;
        private readonly ILogger<InMemoryPublisher> _logger;

        public InMemoryPublisher(IChannelFactory channelFactory, ILogger<InMemoryPublisher> logger)
        {
            _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public Task PublishAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return PublishAsyncCore((dynamic)message, cancellationToken);
        }

        private async Task PublishAsyncCore<TM>(TM message, CancellationToken cancellationToken)
            where TM : IMessage
        {
            var writer = _channelFactory.GetWriter<TM>();
            if (writer is not null)
            {
                _logger.LogInformation($"publishing message '{message.Id}'...");
                await writer.WriteAsync(message, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning($"no suitable publisher found for message '{message.Id}' with type '{typeof(TM).FullName}' !");
            }
                
        }
    }
}