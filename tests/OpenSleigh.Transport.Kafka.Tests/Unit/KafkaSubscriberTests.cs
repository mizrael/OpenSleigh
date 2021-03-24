using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class KafkaSubscriberTests
    {
        [Fact]
        public async Task StartAsync_should_subscribe_to_topic()
        {
            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();

            var sut = BuildSUT(queueRefs, consumer);

            var tokenSource = new CancellationTokenSource(1000);
            await sut.StartAsync(tokenSource.Token);
            
            consumer.Received(1).Subscribe(queueRefs.TopicName);
        }

        [Fact]
        public async Task StartAsync_should_consume_incoming_messages()
        {
            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();

            var sut = BuildSUT(queueRefs, consumer);

            var tokenSource = new CancellationTokenSource(1000);
            await sut.StartAsync(tokenSource.Token);

            consumer.Received().Consume(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_parse_incoming_messages()
        {
            var parser = NSubstitute.Substitute.For<IMessageParser>();
            
            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();

            var consumeResult = new ConsumeResult<Guid, byte[]>();
            consumer.Consume(Arg.Any<CancellationToken>()).Returns(consumeResult);

            var sut = BuildSUT(queueRefs, consumer, parser);

            var tokenSource = new CancellationTokenSource(100);
            await sut.StartAsync(tokenSource.Token);

            parser.Received().Parse(consumeResult);
        }

        [Fact]
        public async Task StartAsync_should_process_incoming_messages()
        {
            var consumeResult = new ConsumeResult<Guid, byte[]>();
            var expectedMessage = NSubstitute.Substitute.For<IMessage>();

            var parser = NSubstitute.Substitute.For<IMessageParser>();
            parser.Parse(consumeResult).Returns(expectedMessage);

            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();
            
            consumer.Consume(Arg.Any<CancellationToken>()).Returns(consumeResult);

            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();

            var sut = BuildSUT(queueRefs, consumer, parser, messageProcessor);

            var tokenSource = new CancellationTokenSource(100);
            await sut.StartAsync(tokenSource.Token);

            await messageProcessor.Received().ProcessAsync((dynamic)expectedMessage, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_republish_to_deadletter_when_exception_occurs()
        {
            var consumeResult = new ConsumeResult<Guid, byte[]>();
            var expectedMessage = NSubstitute.Substitute.For<IMessage>();

            var parser = NSubstitute.Substitute.For<IMessageParser>();
            parser.Parse(consumeResult).Returns(expectedMessage);

            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();

            consumer.Consume(Arg.Any<CancellationToken>()).Returns(consumeResult);

            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();

            var ex = new Exception("whoops");
            messageProcessor.WhenForAnyArgs(mp => mp.ProcessAsync((dynamic)expectedMessage))
                .Throw(ex);

            var expectedErrorHeader = new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message));

            var executor = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();

            var sut = BuildSUT(queueRefs, consumer, parser, messageProcessor, executor);

            var tokenSource = new CancellationTokenSource(100);
            await sut.StartAsync(tokenSource.Token);

            await executor.Received().PublishAsync(expectedMessage, queueRefs.DeadLetterTopicName, 
                Arg.Is( (Header[] headers) => headers.Any(h => h.Key == expectedErrorHeader.Key)), 
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_not_republish_to_deadletter_when_exception_occurs_and_no_deadletter_available()
        {
            var consumeResult = new ConsumeResult<Guid, byte[]>();
            var expectedMessage = NSubstitute.Substitute.For<IMessage>();

            var parser = NSubstitute.Substitute.For<IMessageParser>();
            parser.Parse(consumeResult).Returns(expectedMessage);

            var queueRefs = new QueueReferences("lorem", "");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();

            consumer.Consume(Arg.Any<CancellationToken>()).Returns(consumeResult);

            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
            messageProcessor.WhenForAnyArgs(mp => mp.ProcessAsync((dynamic)expectedMessage))
                .Throw(new Exception("whoops"));

            var executor = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();

            var sut = BuildSUT(queueRefs, consumer, parser, messageProcessor, executor);

            var tokenSource = new CancellationTokenSource(100);
            await sut.StartAsync(tokenSource.Token);

            await executor.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<IMessage>(), 
                Arg.Any<string>(), 
                null,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StopAsync_should_subscribe_to_topic()
        {
            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();

            var sut = BuildSUT(queueRefs, consumer);

            var tokenSource = new CancellationTokenSource(100);
            await sut.StopAsync(tokenSource.Token);

            consumer.Received(1).Unsubscribe();
        }

        private static KafkaSubscriber<IMessage> BuildSUT(
            QueueReferences queueRefs, 
            IConsumer<Guid, byte[]> consumer,
            IMessageParser parser = null,
            IMessageProcessor messageProcessor = null,
            IKafkaPublisherExecutor executor = null)
        {
            var config = new ConsumerConfig()
            {
                GroupId = "group id"
            };
            var builder = NSubstitute.Substitute.ForPartsOf<ConsumerBuilder<Guid, byte[]>>(config);
            builder.When(b => b.Build()).DoNotCallBase();
            builder.Build().Returns(consumer);

            parser ??= NSubstitute.Substitute.For<IMessageParser>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaSubscriber<IMessage>>>();

            var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            queueReferenceFactory.Create<IMessage>().ReturnsForAnyArgs(queueRefs);

            messageProcessor ??= NSubstitute.Substitute.For<IMessageProcessor>();
            executor ??= NSubstitute.Substitute.For<IKafkaPublisherExecutor>();

            return new KafkaSubscriber<IMessage>(builder, parser, logger, queueReferenceFactory, messageProcessor, executor);
        }

    }
}