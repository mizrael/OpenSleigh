using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal sealed class ServiceBusSubscriber<TM> : ISubscriber<TM>, IDisposable
        where TM : IMessage
    {
        private ServiceBusProcessor _processor;
        private readonly IMessageProcessor _messageProcessor;
        private readonly IMessageParser _messageParser;
        private readonly ILogger<ServiceBusSubscriber<TM>> _logger;
        
        public ServiceBusSubscriber(IServiceBusProcessorFactory processorFactory,
            IMessageParser messageParser,
            IMessageProcessor messageProcessor, 
            ILogger<ServiceBusSubscriber<TM>> logger)
        {
            if (processorFactory == null) 
                throw new ArgumentNullException(nameof(processorFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));

            _processor = processorFactory.Create<TM>();
            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += ErrorHandler;
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
            => await _processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);

        public async Task StopAsync(CancellationToken cancellationToken = default)
            => await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);

        public void Dispose()
        {
            _processor = null;
        }
    }
}