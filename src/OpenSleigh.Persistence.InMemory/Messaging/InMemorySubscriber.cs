using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.Factory.StartNew(async () => await ConsumeMessagesAsync(cancellationToken),
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);

        private async Task ConsumeMessagesAsync(CancellationToken cancellationToken)
        {
            await foreach (var message in _reader.ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                try
                {
                    await _messageProcessor.ProcessAsync(message, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogError(e,
                        $"an exception has occurred while processing '{message.GetType().FullName}' message '{message.Id}': {e.Message}");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}