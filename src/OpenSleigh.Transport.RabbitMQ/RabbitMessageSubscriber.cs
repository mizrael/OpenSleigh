﻿using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;

namespace OpenSleigh.Transport.RabbitMQ
{
    public sealed class RabbitMessageSubscriber<TM> : IDisposable, IRabbitMessageSubscriber<TM>
        where TM : IMessage
    {
        private readonly IChannelFactory _channelFactory;
        private readonly QueueReferences _queueReferences;
        private readonly ISerializer _serializer;
        private readonly IMessageProcessor _messageProcessor;
        private readonly ITypeResolver _typeResolver;
        private readonly ILogger<RabbitMessageSubscriber<TM>> _logger;

        private IModel _channel;

        public RabbitMessageSubscriber(IChannelFactory channelFactory,
            IQueueReferenceFactory queueReferenceFactory,
            ISerializer serializer,
            IMessageProcessor messageProcessor,
            ITypeResolver typeResolver,
            ILogger<RabbitMessageSubscriber<TM>> logger)
        {
            _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

            if (queueReferenceFactory == null)
                throw new ArgumentNullException(nameof(queueReferenceFactory));
            _queueReferences = queueReferenceFactory.Create<TM>();
        }

        private void InitChannel()
        {
            StopChannel();

            _channel = _channelFactory.Get(_queueReferences);
            _channel.CallbackException += OnChannelException;
        }

        private void OnChannelException(object _, CallbackExceptionEventArgs ea)
        {
            _logger.LogError(ea.Exception, "the RabbitMQ Channel has encountered an error: {ExceptionMessage}", ea.Exception.Message);
        }

        private void InitSubscription()
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.Received += OnMessageReceivedAsync;

            _logger.LogInformation($"initializing subscription on queue '{_queueReferences.QueueName}' ...");
            _channel.BasicConsume(queue: _queueReferences.QueueName, autoAck: false, consumer: consumer);
        }

        private void StopChannel()
        {
            if (_channel is not null)
                _channel.CallbackException -= OnChannelException;
        }

        private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
        {
            var consumer = sender as IBasicConsumer;
            var channel = consumer?.Model ?? _channel;

            OutboxMessage? message;
            try
            {
                var messageTypeName = eventArgs.BasicProperties.GetHeaderValue(nameof(message.MessageType));
                if (string.IsNullOrWhiteSpace(messageTypeName))
                    throw new ArgumentException("message type cannot be null.");
                
                var messageType = _typeResolver.Resolve(messageTypeName);

                var senderId = eventArgs.BasicProperties.GetHeaderValue(nameof(message.SenderId));
                if (string.IsNullOrWhiteSpace(senderId))
                    throw new ArgumentException("sender id cannot be null.");

                var parentId = eventArgs.BasicProperties.GetHeaderValue(nameof(message.ParentId));

                message = new OutboxMessage()
                {
                    Body = eventArgs.Body,
                    SenderId = senderId,
                    ParentId = parentId,
                    CorrelationId = eventArgs.BasicProperties.CorrelationId,
                    MessageId = eventArgs.BasicProperties.MessageId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    MessageType = messageType
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, 
                    "an exception has occured while decoding queue message from Exchange '{ExchangeName}'. Error: {ExceptionMessage}",
                    eventArgs.Exchange, ex.Message);
                channel.BasicReject(eventArgs.DeliveryTag, requeue: false);
                return;
            }

            _logger.LogInformation(
                "received message '{MessageId}' from Exchange '{ExchangeName}', Queue '{QueueName}'. Processing...",
                message.MessageId, _queueReferences.ExchangeName, _queueReferences.QueueName);

            try
            {
                //TODO: provide valid cancellation token
                await _messageProcessor.ProcessAsync(message, CancellationToken.None);

                channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
            }
            catch (LockException lockEx)
            {
                HandleConsumerException(lockEx, eventArgs, channel, message, true);
            }
            catch (AggregateException aggEx) when (aggEx.InnerExceptions.Any(ex => ex is LockException))
            {
                HandleConsumerException(aggEx, eventArgs, channel, message, true);
            }
            catch (Exception ex)
            {
                HandleConsumerException(ex, eventArgs, channel, message, false);
            }
        }

        private void HandleConsumerException(Exception ex, BasicDeliverEventArgs deliveryProps, IModel channel, OutboxMessage message, bool requeue)
        {
            var errorMsg = "an error has occurred while processing Message '{MessageId}' from Exchange '{ExchangeName}' : {ExceptionMessage} . "
                         + (requeue ? "Reenqueuing..." : "Nacking...");

            _logger.LogWarning(ex, errorMsg, message.MessageId, _queueReferences.ExchangeName, ex.Message);

            if (!requeue)
                channel.BasicReject(deliveryProps.DeliveryTag, requeue: false);
            else
            {
                channel.BasicAck(deliveryProps.DeliveryTag, false);
                channel.BasicPublish(
                    exchange: _queueReferences.RetryExchangeName,
                    routingKey: deliveryProps.RoutingKey,
                    basicProperties: deliveryProps.BasicProperties,
                    body: deliveryProps.Body);
            }
        }

        public void Start()
        {
            InitChannel();
            InitSubscription();
        }

        public void Stop()
        {
            StopChannel();
        }

        public void Dispose()
        {
            StopChannel();
        }
    }
}