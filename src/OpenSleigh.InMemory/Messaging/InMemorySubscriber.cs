using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Threading.Channels;

namespace OpenSleigh.InMemory.Messaging
{
    public class InMemorySubscriber : ISubscriber
    {
        private readonly IMessageProcessor _messageProcessor;
        private readonly ChannelReader<OutboxMessage> _reader;
        private readonly ILogger<InMemorySubscriber> _logger;
        private readonly InMemorySubscriberOptions _options;
        
        private CancellationTokenSource _stoppingCts;
        private Task _consumerTask;

        public InMemorySubscriber(IMessageProcessor messageProcessor,
            ChannelReader<OutboxMessage> reader,
            ILogger<InMemorySubscriber> logger,
            InMemorySubscriberOptions? options = null)
        {
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? InMemorySubscriberOptions.Defaults;
        }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _consumerTask = Task.Run(async () => await ConsumeMessagesAsync(_stoppingCts.Token));

            return ValueTask.CompletedTask;
        }
       
        private async ValueTask ConsumeMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await _reader.ReadMultipleAsync(_options.MaxMessagesBatchSize, cancellationToken)
                                         .ConfigureAwait(false);
                var tasks = batch.Select(async outboxMessage =>
                {
                    try
                    {
                        await _messageProcessor.ProcessAsync(outboxMessage, cancellationToken)
                                               .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e,
                            "an exception has occurred while processing message '{MessageId}': {Error}",
                            outboxMessage.MessageId,
                            e.Message);
                    }
                }).ToArray();

                await Task.WhenAll(tasks);
            }
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            if (_consumerTask == null)
                return;

            try
            {
                _stoppingCts.Cancel();
            }
            finally
            {
                await Task.WhenAny(_consumerTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            }
        }
    }
}