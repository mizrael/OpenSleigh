using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Utils;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Text;
using Xunit;
using NSubstitute;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using FluentAssertions;
using System.Threading;
using System;

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

            var channelContext = _fixture.CreatePublisherContext(channel);
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
            encoder.SerializeAsync(message)
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
    }
}
