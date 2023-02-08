﻿using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ
{
    internal sealed class ChannelFactory : IChannelFactory, IDisposable
    {
        private readonly IBusConnection _connection;
        private readonly ConcurrentDictionary<string, IModel> _channels = new ();
        private readonly RabbitConfiguration _rabbitCfg;
        private readonly ILogger<ChannelFactory> _logger;

        public ChannelFactory(IBusConnection connection, RabbitConfiguration rabbitCfg, ILogger<ChannelFactory> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rabbitCfg = rabbitCfg ?? throw new ArgumentNullException(nameof(rabbitCfg));
        }

        public IModel Get(QueueReferences queueReferences)
        {
            if (queueReferences == null) 
                throw new ArgumentNullException(nameof(queueReferences));

            return _channels.GetOrAdd(queueReferences.ExchangeName, _ =>
            {
                var channel = _connection.CreateChannel();
                _logger.LogInformation($"initializing dead-letter queue '{queueReferences.DeadLetterQueue}' on exchange '{queueReferences.DeadLetterExchangeName}'...");

                channel.ExchangeDeclare(exchange: queueReferences.DeadLetterExchangeName, type: ExchangeType.Topic);
                channel.QueueDeclare(queue: queueReferences.DeadLetterQueue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);
                channel.QueueBind(queueReferences.DeadLetterQueue,
                                  queueReferences.DeadLetterExchangeName,
                                  routingKey: queueReferences.DeadLetterQueue,
                                  arguments: null);

                _logger.LogInformation($"initializing retry queue '{queueReferences.RetryQueueName}' on exchange '{queueReferences.RetryExchangeName}'...");

                channel.ExchangeDeclare(exchange: queueReferences.RetryExchangeName, type: ExchangeType.Topic);
                channel.QueueDeclare(queue: queueReferences.RetryQueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object>()
                    {
                    {Headers.XMessageTTL, (int)_rabbitCfg.RetryDelay.TotalMilliseconds },
                    {Headers.XDeadLetterExchange, queueReferences.ExchangeName},
                    {Headers.XDeadLetterRoutingKey, queueReferences.QueueName}
                    });
                channel.QueueBind(queue: queueReferences.RetryQueueName,
                    exchange: queueReferences.RetryExchangeName,
                    routingKey: queueReferences.RoutingKey,
                    arguments: null);

                _logger.LogInformation($"initializing queue '{queueReferences.QueueName}' on exchange '{queueReferences.ExchangeName}'...");

                channel.ExchangeDeclare(exchange: queueReferences.ExchangeName, type: ExchangeType.Topic);
                channel.QueueDeclare(queue: queueReferences.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object>()
                    {
                    {Headers.XDeadLetterExchange, queueReferences.DeadLetterExchangeName},
                    {Headers.XDeadLetterRoutingKey, queueReferences.DeadLetterQueue}
                    });
                channel.QueueBind(queue: queueReferences.QueueName,
                    exchange: queueReferences.ExchangeName,
                    routingKey: queueReferences.RoutingKey,
                    arguments: null);
                return channel;
            });
        }
               
        public void Dispose()
        {
            foreach (var channel in _channels.Values)
            {
                if (channel.IsOpen)
                    channel.Close();
                channel.Dispose();
            }
            _channels.Clear();
        }
    }
}