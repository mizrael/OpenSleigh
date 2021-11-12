using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
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
    public class RabbitSubscriberTests : IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _fixture;

        public RabbitSubscriberTests(RabbitFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StartAsync_should_consume_messages()
        {
            var message = DummyMessage.New();
            var encodedMessage = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(message));

            using var connection = _fixture.Connect();
            using var channel = connection.CreateModel();
            var queueRef = _fixture.CreateQueueReference("test_publisher");

            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var busConn = Substitute.For<IBusConnection>(); 
            busConn.CreateChannel()
                .Returns(channel);

            var queueRefFactory = Substitute.For<IQueueReferenceFactory>();
            queueRefFactory.Create<DummyMessage>()
                .ReturnsForAnyArgs(queueRef);

            bool received = false;
            var messageParser = Substitute.For<IMessageParser>();
            messageParser.When(p => p.Resolve(Arg.Any<IBasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>()))
                .Do(p =>
                {
                    received = true;    
                    tokenSource.Cancel();
                });
            
            messageParser.Resolve(null, null)
                .ReturnsForAnyArgs(message);

            var processor = Substitute.For<IMessageProcessor>();
            var logger = Substitute.For<ILogger<RabbitSubscriber<DummyMessage>>>();

            var sut = new RabbitSubscriber<DummyMessage>(busConn, queueRefFactory, messageParser,
                                                        processor, logger, _fixture.RabbitConfiguration);

            await sut.StartAsync();

            var props = channel.CreateBasicProperties();
            channel.BasicPublish(queueRef.ExchangeName, queueRef.QueueName, false, props, encodedMessage);

            while (!tokenSource.IsCancellationRequested)
                await Task.Delay(10);

            received.Should().BeTrue();
        }
    }
}
