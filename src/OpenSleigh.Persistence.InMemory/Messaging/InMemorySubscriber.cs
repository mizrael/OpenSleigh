using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.InMemory.Messaging
{
    public record InMemorySubscriberOptions
    {
        /// <summary>
        /// max size of the message batches processed concurrently by each subscriber.
        /// </summary>
        public int MaxMessagesBatchSize { get; }

        public InMemorySubscriberOptions(int messagesBatchSize)
        {
            MaxMessagesBatchSize = messagesBatchSize;
        }

        public static readonly InMemorySubscriberOptions Defaults = new InMemorySubscriberOptions(5);
    }

    public class InMemorySubscriber<TM> : ISubscriber
        where TM : IMessage
    {
        private readonly IMessageProcessor _messageProcessor;
        private readonly ChannelReader<TM> _reader;
        private readonly ILogger<InMemorySubscriber<TM>> _logger;
        private readonly InMemorySubscriberOptions _options;
        private CancellationTokenSource _stoppingCts;
        private Task _consumerTask;

        public InMemorySubscriber(IMessageProcessor messageProcessor,
            ChannelReader<TM> reader,
            ILogger<InMemorySubscriber<TM>> logger,
            InMemorySubscriberOptions options = null)
        {
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? InMemorySubscriberOptions.Defaults;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _consumerTask = Task.Run(async () => await ConsumeMessagesAsync(_stoppingCts.Token));

            return Task.CompletedTask;
        }

        private async Task ConsumeMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await _reader.ReadMultipleAsync(_options.MaxMessagesBatchSize, cancellationToken);
                var tasks = batch.Select(async message =>
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
                }).ToArray();

                await Task.WhenAll(tasks);  
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
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