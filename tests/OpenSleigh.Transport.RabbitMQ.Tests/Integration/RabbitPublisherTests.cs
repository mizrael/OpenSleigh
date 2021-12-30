using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.Utils;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Integration
{
    [Category("Integration")]
    [Trait("Category", "Integration")]
    public class RabbitPublisherTests : IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _fixture;

        public RabbitPublisherTests(RabbitFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task PublishAsync_should_publish_message()
        {
            var message = DummyMessage.New();
            var encodedMessage = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(message));

            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            using var connection = _fixture.Connect();
            using var channel = connection.CreateModel();

            var channelContext = CreatePublisherContext(channel);
            bool received = false;
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (_, evt) =>
            {
                evt.Body.Should().NotBeNull();
                evt.Body.ToArray().Should().BeEquivalentTo(encodedMessage);
                received = true;

                tokenSource.Cancel();
            };
            channel.BasicConsume(queue: channelContext.QueueReferences.QueueName, autoAck: false, consumer: consumer);

            var encoder = Substitute.For<ITransportSerializer>();
            encoder.Serialize(message)
                .Returns(encodedMessage);

            var logger = Substitute.For<ILogger<RabbitPublisher>>();

            var publisherChannelFactory = NSubstitute.Substitute.For<IPublisherChannelFactory>();
            publisherChannelFactory.Create(message)
                .Returns(channelContext);

            var sut = new RabbitPublisher(encoder, logger, publisherChannelFactory);
            await sut.PublishAsync(message);

            while (!tokenSource.IsCancellationRequested)
                await Task.Delay(10);

            received.Should().BeTrue(); 
        }

        private PublisherChannelContext CreatePublisherContext(IModel channel)
        {
            var queueName = System.Guid.NewGuid().ToString();         

            var pool = Substitute.For<IPublisherChannelContextPool>();
            var queueRef = _fixture.CreateQueueReference(queueName);

            channel.ExchangeDeclare(queueRef.ExchangeName, ExchangeType.Topic, false, true);
            channel.QueueDeclare(queue: queueRef.QueueName,
                durable: false,
                exclusive: false,
                autoDelete: true,
                arguments: null);
            channel.QueueBind(queueRef.QueueName,
                              queueRef.ExchangeName,
                              routingKey: queueRef.RoutingKey,
                              arguments: null);

            return new PublisherChannelContext(channel, queueRef, pool);
        }


    }
}
