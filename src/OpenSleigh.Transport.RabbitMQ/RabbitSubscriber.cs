using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public sealed class RabbitSubscriber<TM> : ISubscriber<TM>, IDisposable
        where TM : IMessage
    {
        private readonly IBusConnection _connection;
        private readonly QueueReferences _queueReferences;
        private readonly IMessageParser _messageParser;
        private readonly IMessageProcessor _messageProcessor;
        private readonly ILogger<RabbitSubscriber<TM>> _logger;
        private readonly RabbitConfiguration _rabbitCfg;
        private IModel _channel;

        public RabbitSubscriber(IBusConnection connection,
            IQueueReferenceFactory queueReferenceFactory,
            IMessageParser messageParser,
            IMessageProcessor messageProcessor,
            ILogger<RabbitSubscriber<TM>> logger, 
            RabbitConfiguration rabbitCfg)
        {
            if (queueReferenceFactory == null) throw new ArgumentNullException(nameof(queueReferenceFactory));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _queueReferences = queueReferenceFactory.Create<TM>();
            _rabbitCfg = rabbitCfg ?? throw new ArgumentNullException(nameof(rabbitCfg));
        }

        private void InitChannel()
        {
            StopChannel();

            _channel = _connection.CreateChannel();
            
            _logger.LogInformation($"initializing dead-letter queue '{_queueReferences.DeadLetterQueue}' on exchange '{_queueReferences.DeadLetterExchangeName}'...");

            _channel.ExchangeDeclare(exchange: _queueReferences.DeadLetterExchangeName, type: ExchangeType.Topic);
            _channel.QueueDeclare(queue: _queueReferences.DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);
            _channel.QueueBind(_queueReferences.DeadLetterQueue, 
                              _queueReferences.DeadLetterExchangeName, 
                              routingKey: _queueReferences.DeadLetterQueue, 
                              arguments: null);

            _logger.LogInformation($"initializing retry queue '{_queueReferences.RetryQueueName}' on exchange '{_queueReferences.RetryExchangeName}'...");

            _channel.ExchangeDeclare(exchange: _queueReferences.RetryExchangeName, type: ExchangeType.Topic);
            _channel.QueueDeclare(queue: _queueReferences.RetryQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>()
                {
                    {Headers.XMessageTTL, (int)_rabbitCfg.RetryDelay.TotalMilliseconds },
                    {Headers.XDeadLetterExchange, _queueReferences.ExchangeName},
                    {Headers.XDeadLetterRoutingKey, _queueReferences.QueueName}
                });
            _channel.QueueBind(queue: _queueReferences.RetryQueueName,
                exchange: _queueReferences.RetryExchangeName,
                routingKey: _queueReferences.RoutingKey,
                arguments: null);

            _logger.LogInformation($"initializing queue '{_queueReferences.QueueName}' on exchange '{_queueReferences.ExchangeName}'...");
            
            _channel.ExchangeDeclare(exchange: _queueReferences.ExchangeName, type: ExchangeType.Topic);
            _channel.QueueDeclare(queue: _queueReferences.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>()
                {
                    {Headers.XDeadLetterExchange, _queueReferences.DeadLetterExchangeName},
                    {Headers.XDeadLetterRoutingKey, _queueReferences.DeadLetterQueue}
                });
            _channel.QueueBind(queue: _queueReferences.QueueName,
                exchange: _queueReferences.ExchangeName, 
                routingKey: _queueReferences.RoutingKey, 
                arguments: null);

            _channel.CallbackException += OnChannelException;
        }

        private void OnChannelException(object _, CallbackExceptionEventArgs ea)
        {
            _logger.LogError(ea.Exception, "the RabbitMQ Channel has encountered an error: {ExceptionMessage}", ea.Exception.Message);

            InitChannel();
            InitSubscription();
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
            if (_channel is null)
                return;

            _channel.CallbackException -= OnChannelException;

            if (_channel.IsOpen)
                _channel.Close();

            _channel.Dispose();
            _channel = null;
        }

        private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
        {
            var consumer = sender as IBasicConsumer;
            var channel = consumer?.Model ?? _channel;

            IMessage message;
            try
            {
                message = _messageParser.Resolve(eventArgs.BasicProperties, eventArgs.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "an exception has occured while decoding queue message from Exchange '{ExchangeName}', message cannot be parsed. Error: {ExceptionMessage}",
                    eventArgs.Exchange, ex.Message);
                channel.BasicReject(eventArgs.DeliveryTag, requeue: false);
                return;
            }

            _logger.LogInformation("received message '{MessageId}' from Exchange '{ExchangeName}', Queue '{QueueName}'. Processing...", 
                message.Id, _queueReferences.ExchangeName, _queueReferences.QueueName);

            try
            {
                //TODO: provide valid cancellation token
                await _messageProcessor.ProcessAsync((dynamic)message, CancellationToken.None);

                channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
            }
            catch(Core.Exceptions.LockException lockEx)
            {
                HandleConsumerException(lockEx, eventArgs, channel, message, true);
            }
            catch(AggregateException aggEx) when(aggEx.InnerExceptions.Any(ex => ex is Core.Exceptions.LockException))
            {
                HandleConsumerException(aggEx, eventArgs, channel, message, true);
            }
            catch (Exception ex)
            {
                HandleConsumerException(ex, eventArgs, channel, message, false);
            }
        }

        private void HandleConsumerException(Exception ex, BasicDeliverEventArgs deliveryProps, IModel channel, IMessage message, bool requeue)
        {
            var errorMsg = "an error has occurred while processing Message '{MessageId}' from Exchange '{ExchangeName}' : {ExceptionMessage} . "
                         + (requeue ? "Reenqueuing..." : "Nacking...");

            _logger.LogWarning(ex, errorMsg, message.Id, _queueReferences.ExchangeName, ex.Message);

            if(!requeue)
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

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            InitChannel();
            InitSubscription();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopChannel();
            return Task.CompletedTask;
        }
        
        public void Dispose()
        {
            StopChannel();
        } 
    }
}