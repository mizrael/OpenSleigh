using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Tests;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using OpenSleigh.Utils;
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
    public class RabbitMessageSubscriberTests : IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _fixture;

        public RabbitMessageSubscriberTests(RabbitFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StartAsync_should_consume_messages()
        {
            var sagaContext = NSubstitute.Substitute.For<ISagaExecutionContext>();
            sagaContext.CorrelationId.Returns(Guid.NewGuid().ToString());
            sagaContext.TriggerMessageId.Returns(Guid.NewGuid().ToString());
            sagaContext.InstanceId.Returns(Guid.NewGuid().ToString());

            var serializer = new JsonSerializer();

            var message = OutboxMessage.Create(new FakeSagaStarter(), serializer, sagaContext);

            using var connection = _fixture.Connect();
            using var channel = connection.CreateModel();

            var queueRef = _fixture.CreateQueueReference(channel);

            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var busConn = Substitute.For<IBusConnection>();
            busConn.CreateChannel()
                .Returns(channel);

            var queueRefFactory = Substitute.For<IQueueReferenceFactory>();
            queueRefFactory.Create(message).Returns(queueRef);
            queueRefFactory.Create<FakeSagaStarter>().Returns(queueRef);

            bool received = false;
            var processor = Substitute.For<IMessageProcessor>();
            processor.When(p => p.ProcessAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>()))
                .Do(p =>
                {
                    received = true;
                    tokenSource.Cancel();
                });
            
            var channelFactory = Substitute.For<IChannelFactory>();
            channelFactory.Get(queueRef)
                .Returns(channel);

            var typeResolver = Substitute.For<ITypeResolver>();

            var logger = Substitute.For<ILogger<RabbitMessageSubscriber<FakeSagaStarter>>>();

            var sut = new RabbitMessageSubscriber<FakeSagaStarter>(channelFactory, queueRefFactory, serializer, processor, typeResolver, logger);

            sut.Start();

            var publisher = new RabbitPublisher(
                serializer,
                Substitute.For<ILogger<RabbitPublisher>>(),
                queueRefFactory,
                channelFactory);
            await publisher.PublishAsync(message);

            while (!tokenSource.IsCancellationRequested)
                await Task.Delay(10);

            received.Should().BeTrue();
        }

        //[Fact]
        //public async Task StartAsync_should_retry_message_when_locked()
        //{
        //    var message = DummyMessage.New();
        //    var encodedMessage = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(message));

        //    using var connection = _fixture.Connect();
        //    using var channel = connection.CreateModel();
        //    var queueRef = _fixture.CreateQueueReference("test_publisher");

        //    var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        //    var busConn = Substitute.For<IBusConnection>();
        //    busConn.CreateChannel()
        //        .Returns(channel);

        //    var queueRefFactory = Substitute.For<IQueueReferenceFactory>();
        //    queueRefFactory.Create<DummyMessage>()
        //        .ReturnsForAnyArgs(queueRef);

        //    var messageParser = Substitute.For<IMessageParser>();
        //    messageParser.Resolve(null, null)
        //        .ReturnsForAnyArgs(message);

        //    var processCount = 0;
        //    var processor = Substitute.For<IMessageProcessor>();
        //    processor.When(p => p.ProcessAsync(Arg.Any<DummyMessage>(), Arg.Any<CancellationToken>()))
        //        .Do(p =>
        //         {
        //             processCount++;
        //             if (1 == processCount)
        //                 throw new LockException("whoops");

        //             tokenSource.Cancel();
        //         });

        //    var logger = Substitute.For<ILogger<RabbitSubscriber<DummyMessage>>>();

        //    var sut = new RabbitSubscriber<DummyMessage>(busConn, queueRefFactory, messageParser,
        //                                                processor, logger, _fixture.RabbitConfiguration);

        //    await sut.StartAsync();

        //    var props = channel.CreateBasicProperties();
        //    channel.BasicPublish(queueRef.ExchangeName, queueRef.QueueName, false, props, encodedMessage);

        //    while (!tokenSource.IsCancellationRequested)
        //        await Task.Delay(10);

        //    processCount.Should().BeGreaterThan(0);
        //}

        //[Fact]
        //public async Task StartAsync_should_retry_message_when_AggregateException_with_lock()
        //{
        //    var message = DummyMessage.New();
        //    var encodedMessage = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(message));

        //    using var connection = _fixture.Connect();
        //    using var channel = connection.CreateModel();
        //    var queueRef = _fixture.CreateQueueReference("test_publisher");

        //    var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        //    var busConn = Substitute.For<IBusConnection>();
        //    busConn.CreateChannel()
        //        .Returns(channel);

        //    var queueRefFactory = Substitute.For<IQueueReferenceFactory>();
        //    queueRefFactory.Create<DummyMessage>()
        //        .ReturnsForAnyArgs(queueRef);

        //    var messageParser = Substitute.For<IMessageParser>();
        //    messageParser.Resolve(null, null)
        //        .ReturnsForAnyArgs(message);

        //    var processCount = 0;
        //    var processor = Substitute.For<IMessageProcessor>();
        //    processor.When(p => p.ProcessAsync(Arg.Any<DummyMessage>(), Arg.Any<CancellationToken>()))
        //        .Do(p =>
        //        {
        //            processCount++;
        //            if (1 == processCount)
        //                throw new AggregateException(new LockException("whoops"));

        //            tokenSource.Cancel();
        //        });

        //    var logger = Substitute.For<ILogger<RabbitSubscriber<DummyMessage>>>();

        //    var sut = new RabbitSubscriber<DummyMessage>(busConn, queueRefFactory, messageParser,
        //                                                processor, logger, _fixture.RabbitConfiguration);

        //    await sut.StartAsync();

        //    var props = channel.CreateBasicProperties();
        //    channel.BasicPublish(queueRef.ExchangeName, queueRef.QueueName, false, props, encodedMessage);

        //    while (!tokenSource.IsCancellationRequested)
        //        await Task.Delay(10);

        //    processCount.Should().BeGreaterThan(0);
        //}
    }
}
