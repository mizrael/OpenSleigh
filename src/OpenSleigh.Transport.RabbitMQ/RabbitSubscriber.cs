using OpenSleigh.Core;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class RabbitSubscriber<TM> : IAsyncDisposable, ISubscriber<TM>
        where TM : IMessage
    {
        private readonly IBusConnection _connection;
        private readonly QueueReferences _queueReferences;
        private readonly IMessageParser _messageParser;
        private readonly IMessageProcessor _messageProcessor;
        private readonly ILogger<RabbitSubscriber<TM>> _logger;
        private IModel _channel;

        public RabbitSubscriber(IBusConnection connection,
            IQueueReferenceFactory queueReferenceFactory,
            IMessageParser messageParser,
            IMessageProcessor messageProcessor,
            ILogger<RabbitSubscriber<TM>> logger)
        {
            if (queueReferenceFactory == null) throw new ArgumentNullException(nameof(queueReferenceFactory));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _queueReferences = queueReferenceFactory.Create<TM>();
        }

        private void InitChannel()
        {
            StopChannel();

            _channel = _connection.CreateChannel();

            _channel.ExchangeDeclare(exchange: _queueReferences.DeadLetterExchangeName, type: ExchangeType.Fanout);
            _channel.QueueDeclare(queue: _queueReferences.DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);
            _channel.QueueBind(_queueReferences.DeadLetterQueue, _queueReferences.DeadLetterExchangeName, routingKey: string.Empty, arguments: null);

            _channel.ExchangeDeclare(exchange: _queueReferences.ExchangeName, type: ExchangeType.Fanout);
            _channel.QueueDeclare(queue: _queueReferences.QueueName,
                durable: false,
                exclusive: false,
                autoDelete: true,
                arguments: new Dictionary<string, object>()
                {
                    {Headers.XDeadLetterExchange, _queueReferences.DeadLetterExchangeName},
                    {Headers.XDeadLetterRoutingKey, _queueReferences.ExchangeName}
                });
            _channel.QueueBind(_queueReferences.QueueName, _queueReferences.ExchangeName, string.Empty, null);

            _channel.CallbackException += OnChannelException;
        }

        private void OnChannelException(object? sender, CallbackExceptionEventArgs ea)
        {
            _logger.LogError(ea.Exception, "the RabbitMQ Channel has encountered an error: {ExceptionMessage}", ea.Exception.Message);

            InitChannel();
            InitSubscription();
        }

        private void InitSubscription()
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.Received += OnMessageReceivedAsync;

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

            TM message;
            try
            {
                message = _messageParser.Resolve<TM>(eventArgs.BasicProperties, eventArgs.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "an exception has occured while decoding queue message from Exchange '{ExchangeName}', message cannot be processed. Error: {ExceptionMessage}",
                    eventArgs.Exchange, ex.Message);
                channel.BasicReject(eventArgs.DeliveryTag, requeue: false);
                return;
            }

            _logger.LogDebug("received message '{MessageId}' from Exchange '{ExchangeName}'. Processing...", message.Id, _queueReferences.ExchangeName);

            try
            {
                //TODO: provide valid cancellation token
                await _messageProcessor.ProcessAsync(message, CancellationToken.None);

                channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                var errorMsg = eventArgs.Redelivered ? "a fatal error has occurred while processing Message '{MessageId}' from Exchange '{ExchangeName}' : {ExceptionMessage} . Rejecting..." :
                    "an error has occurred while processing Message '{MessageId}' from Exchange '{ExchangeName}' : {ExceptionMessage} . Nacking...";

                _logger.LogWarning(ex, errorMsg, message.Id, _queueReferences.ExchangeName, ex.Message);

                if (eventArgs.Redelivered)
                    channel.BasicReject(eventArgs.DeliveryTag, requeue: false);
                else
                    channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            InitChannel();
            InitSubscription();
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopChannel();
        }

        public ValueTask DisposeAsync() => new ValueTask(StopAsync());
    }
}