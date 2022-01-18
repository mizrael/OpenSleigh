using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal sealed class ServiceBusSubscriber<TM> : ISubscriber<TM>, IAsyncDisposable
        where TM : IMessage
    {
        private readonly IMessageProcessor _messageProcessor;
        private readonly ITransportSerializer _messageParser;        
        private readonly ILogger<ServiceBusSubscriber<TM>> _logger;
        private readonly ISystemInfo _systemInfo;
        private ServiceBusProcessor _processor;

        public ServiceBusSubscriber(IQueueReferenceFactory queueReferenceFactory,
            ServiceBusClient serviceBusClient,
            ITransportSerializer messageParser,
            IMessageProcessor messageProcessor,
            ILogger<ServiceBusSubscriber<TM>> logger,
            AzureServiceBusConfiguration sbConfig,
            ISystemInfo systemInfo)
        {
            if (queueReferenceFactory is null)            
                throw new ArgumentNullException(nameof(queueReferenceFactory));
            if (serviceBusClient is null)            
                throw new ArgumentNullException(nameof(serviceBusClient));            
            if (sbConfig is null)            
                throw new ArgumentNullException(nameof(sbConfig));           

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));

            var references = queueReferenceFactory.Create<TM>();
            _processor = serviceBusClient.CreateProcessor(
                topicName: references.TopicName,
                subscriptionName: references.SubscriptionName, new ServiceBusProcessorOptions()
                {
                    AutoCompleteMessages = false,
                    MaxConcurrentCalls = sbConfig.MaxConcurrentCalls
                });
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

                var message = await _messageParser.DeserializeAsync<TM>(args.Message.Body.ToStream());

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

#if !DEBUG
        public async ValueTask DisposeAsync()
        {
            // calling DisposeAsync() on each processor might take a long time to complete (~60sec) due to 
            // apparent limitations of the underlying AMQP library.
            // more details here: https://github.com/Azure/azure-sdk-for-net/issues/19306
            // Therefore we do it only when in Release mode. Debug mode is used when running the tests suite.
            await _processor.StopProcessingAsync();            
            _processor.ProcessMessageAsync -= MessageHandler;
            _processor.ProcessErrorAsync -= ProcessErrorAsync;
            await _processor.DisposeAsync();
            _processor = null;
        }
#else
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
#endif

    }
}