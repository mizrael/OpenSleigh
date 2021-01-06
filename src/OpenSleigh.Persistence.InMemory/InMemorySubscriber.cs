using OpenSleigh.Core;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Persistence.InMemory
{
    internal class InMemorySubscriber<TM> : ISubscriber
        where TM : IMessage
    {
        private readonly IMessageProcessor _messageProcessor;
        private readonly ChannelReader<TM> _reader;

        public InMemorySubscriber(IMessageProcessor messageProcessor, ChannelReader<TM> reader)
        {
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var message in _reader.ReadAllAsync(cancellationToken))
            {
                await _messageProcessor.ProcessAsync(message, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

    }
}