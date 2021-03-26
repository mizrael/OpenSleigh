using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal sealed class ServiceBusSubscriber<TM> : ISubscriber<TM>, IAsyncDisposable
        where TM : IMessage
    {
        private readonly IMessageProcessor _messageProcessor;
        private readonly IMessageParser _messageParser;        
        private readonly ILogger<ServiceBusSubscriber<TM>> _logger;
        private readonly SystemInfo _systemInfo;
        private ServiceBusProcessor _processor;

        public ServiceBusSubscriber(IQueueReferenceFactory queueReferenceFactory,
            ServiceBusClient serviceBusClient,
            IMessageParser messageParser,
            IMessageProcessor messageProcessor,
            ILogger<ServiceBusSubscriber<TM>> logger, SystemInfo systemInfo)
        {            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));

            var references = queueReferenceFactory.Create<TM>();
            _processor = serviceBusClient.CreateProcessor(references.TopicName, references.SubscriptionName);
            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += ProcessErrorAsync;
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs arg)
        {
            _logger.LogError(arg.Exception, $"an exception has occurred while processing a message on client '{_systemInfo.ClientId}'");
            return Task.CompletedTask;
        }

        private async Task MessageHandler(ProcessMessageEventArgs args)
        {
            try
            {
                _logger.LogInformation($"client '{_systemInfo.ClientId}' received message '{args.Message.MessageId}'. Processing...");

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
            await _processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation($"subscriber started on client '{_systemInfo.ClientId}' for '{_processor.EntityPath}'");
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
#if !DEBUG
            // calling DisposeAsync() on each processor might take a long time to complete (~60sec) due to 
            // apparent limitations of the underlying AMQP library.
            // more details here: https://github.com/Azure/azure-sdk-for-net/issues/19306
            // Therefore we do it only when in Release mode. Debug mode is used when running the tests suite.
            await _processor.StartProcessingAsync();            
            _processor.ProcessMessageAsync -= MessageHandler;
            _processor.ProcessErrorAsync -= ProcessErrorAsync;
            await _processor.DisposeAsync();
#endif

            _processor = null;
        }
    }
}