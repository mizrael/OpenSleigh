using OpenSleigh.Core;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class RabbitPublisher<TM> : IPublisher<TM>, IDisposable
        where TM : IMessage
    {
        private readonly IBusConnection _connection;
        private readonly QueueReferences _queueReferences;
        private readonly ILogger<RabbitPublisher<TM>> _logger;
        private readonly IEncoder _encoder;
        private IModel _channel;

        public RabbitPublisher(IBusConnection connection,
            IQueueReferenceFactory queueReferenceFactory,
            IEncoder encoder,
            ILogger<RabbitPublisher<TM>> logger)
        {
            if (queueReferenceFactory == null) throw new ArgumentNullException(nameof(queueReferenceFactory));
            _queueReferences = queueReferenceFactory.Create<TM>();

            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        private void InitChannel()
        {
            if (_channel is not null && _channel.IsOpen)
                return;

            _channel = _connection.CreateChannel();
            _channel.ExchangeDeclare(exchange: _queueReferences.ExchangeName, type: ExchangeType.Fanout);
        }

        public async Task PublishAsync(TM message, CancellationToken cancellationToken = default)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            InitChannel();

            var encodedMessage = _encoder.Encode(message);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Headers = new Dictionary<string, object>()
            {
                {HeaderNames.MessageType, message.GetType().FullName }
            };

            var policy = Policy.Handle<Exception>()
                .WaitAndRetry(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogWarning(ex, "Could not publish message '{MessageId}' to Exchange '{ExchangeName}' after {Timeout}s : {ExceptionMessage}", message.Id, _queueReferences.ExchangeName, $"{time.TotalSeconds:n1}", ex.Message);
                });

            policy.Execute(() =>
            {
                _channel.BasicPublish(
                    exchange: _queueReferences.ExchangeName,
                    routingKey: string.Empty,
                    mandatory: true,
                    basicProperties: properties,
                    body: encodedMessage.Value);

                _logger.LogInformation("message '{MessageId}' published to Exchange '{ExchangeName}'", message.Id, _queueReferences.ExchangeName);
            });
        }

        public void Dispose()
        {
            if (_channel is null)
                return;

            if (_channel.IsOpen)
                _channel.Close();

            _channel.Dispose();
            _channel = null;
        }
    }
}