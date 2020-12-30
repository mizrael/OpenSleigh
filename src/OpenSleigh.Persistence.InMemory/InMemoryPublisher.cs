using OpenSleigh.Core;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("OpenSleigh.Persistence.InMemory.Tests")]
namespace OpenSleigh.Persistence.InMemory
{
    internal class InMemoryPublisher<TM> : IPublisher<TM>
        where TM : IMessage
    {
        private readonly ChannelWriter<TM> _writer;

        public InMemoryPublisher(ChannelWriter<TM> writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public async Task PublishAsync(TM message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            await _writer.WriteAsync(message, cancellationToken)
                        .ConfigureAwait(false);
        }
    }
}