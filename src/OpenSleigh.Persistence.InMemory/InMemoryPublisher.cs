using OpenSleigh.Core;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("OpenSleigh.Persistence.InMemory.Tests")]
namespace OpenSleigh.Persistence.InMemory
{
    internal class InMemoryPublisher : IPublisher 
    {
        private readonly IChannelFactory _channelFactory;

        public InMemoryPublisher(IChannelFactory channelFactory)
        {
            _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
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
            if (null == writer)
                return;

            await writer.WriteAsync(message, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}