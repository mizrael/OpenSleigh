using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Tests;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using OpenSleigh.Utils;
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
            var sagaContext = NSubstitute.Substitute.For<ISagaExecutionContext>();
            sagaContext.CorrelationId.Returns(Guid.NewGuid().ToString());
            sagaContext.TriggerMessageId.Returns(Guid.NewGuid().ToString());
            sagaContext.InstanceId.Returns(Guid.NewGuid().ToString());

            var message = OutboxMessage.Create(new FakeSagaStarter(), new JsonSerializer(), sagaContext);
            var encoder = new JsonSerializer();

            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            using var connection = _fixture.Connect();
            using var channel = connection.CreateModel();

            var queueRef = _fixture.CreateQueueReference(channel);

            bool received = false;
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (_, evt) =>
            {
                evt.Body.Should().NotBeNull();
                evt.Body.ToArray().Should().BeEquivalentTo(message.Body.ToArray());

                evt.BasicProperties.Headers.Should().NotBeNullOrEmpty();
                evt.BasicProperties.Headers.Should().ContainKeys(
                    nameof(OutboxMessage.ParentId),
                    nameof(OutboxMessage.SenderId),                    
                    nameof(OutboxMessage.CreatedAt),
                    nameof(OutboxMessage.MessageType)
                );
                evt.BasicProperties.CorrelationId.Should().Be(message.CorrelationId);
                evt.BasicProperties.MessageId.Should().Be(message.MessageId);
                evt.BasicProperties.Headers[nameof(OutboxMessage.MessageType)].Should().BeEquivalentTo(Encoding.UTF8.GetBytes(typeof(FakeSagaStarter).FullName));                
                evt.BasicProperties.Headers[nameof(OutboxMessage.CreatedAt)].Should().BeEquivalentTo(Encoding.UTF8.GetBytes(message.CreatedAt.ToString()));
                evt.BasicProperties.Headers[nameof(OutboxMessage.ParentId)].Should().BeEquivalentTo(Encoding.UTF8.GetBytes(message.ParentId));
                evt.BasicProperties.Headers[nameof(OutboxMessage.SenderId)].Should().BeEquivalentTo(Encoding.UTF8.GetBytes(message.SenderId));

                received = true;

                tokenSource.Cancel();
            };
            channel.BasicConsume(queue: queueRef.QueueName, autoAck: false, consumer: consumer);

            var logger = Substitute.For<ILogger<RabbitPublisher>>();

            var channelFactory = Substitute.For<IChannelFactory>();
            channelFactory.Get(queueRef)
                .Returns(channel);

            var queueRefFactory = Substitute.For<IQueueReferenceFactory>();
            queueRefFactory.Create(message)
                .Returns(queueRef);

            var sut = new RabbitPublisher(encoder, logger, queueRefFactory, channelFactory);
            await sut.PublishAsync(message);

            while (!tokenSource.IsCancellationRequested)
                await Task.Delay(10);

            received.Should().BeTrue(); 
        }
    }
}
