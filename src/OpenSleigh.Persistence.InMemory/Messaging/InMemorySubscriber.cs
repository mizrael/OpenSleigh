using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Persistence.InMemory.Messaging
{
    public class InMemorySubscriber<TM> : ISubscriber
        where TM : IMessage
    {
        private readonly IMessageProcessor _messageProcessor;
        private readonly ChannelReader<TM> _reader;
        private readonly ILogger<InMemorySubscriber<TM>> _logger;
        
        public InMemorySubscriber(IMessageProcessor messageProcessor, 
            ChannelReader<TM> reader, 
            ILogger<InMemorySubscriber<TM>> logger)
        {
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var message in _reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await _messageProcessor.ProcessAsync(message, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e,
                        $"an exception has occurred while processing message '{message.Id}': {e.Message}");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}