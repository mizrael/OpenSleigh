using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal sealed class ServiceBusSubscriber<TM> : ISubscriber<TM>
        where TM : IMessage
    {
        private readonly IMessageProcessor _messageProcessor;
        private readonly IMessageParser _messageParser;
        private readonly IServiceBusProcessorFactory _processorFactory;
        private readonly ILogger<ServiceBusSubscriber<TM>> _logger;
        
        public ServiceBusSubscriber(IServiceBusProcessorFactory processorFactory,
            IMessageParser messageParser,
            IMessageProcessor messageProcessor, 
            ILogger<ServiceBusSubscriber<TM>> logger)
        {
            _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory)); ;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
        }

        private ServiceBusProcessor CreateProcessor()
        {
            var processor = _processorFactory.Create<TM>();
            processor.ProcessMessageAsync += MessageHandler;
            processor.ProcessErrorAsync += ErrorHandler;
            return processor;
        }

        private Task ErrorHandler(ProcessErrorEventArgs args)
        {
            _logger.LogError(args.Exception, $"an error has occurred while processing messages: {args.Exception.Message}");
            return Task.CompletedTask;
        }

        private async Task MessageHandler(ProcessMessageEventArgs args)
        {
            try
            {
                var message = _messageParser.Resolve<TM>(args.Message.Body);

                await _messageProcessor.ProcessAsync((dynamic)message, args.CancellationToken);

                await args.CompleteMessageAsync(args.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"an error has occurred while processing message '{args.Message.MessageId}': {ex.Message}");
                if (args.Message.DeliveryCount > 3)
                    await args.DeadLetterMessageAsync(args.Message).ConfigureAwait(false);
                else
                    await args.AbandonMessageAsync(args.Message).ConfigureAwait(false);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            var processor = CreateProcessor();
            if(!processor.IsProcessing)
                await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}