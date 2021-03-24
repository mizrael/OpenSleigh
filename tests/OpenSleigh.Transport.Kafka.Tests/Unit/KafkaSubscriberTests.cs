using System;
using System.Collections.Generic;
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
        public async Task StartAsync_should_process_incoming_messages()
        {
            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumeResult = new ConsumeResult<Guid, byte[]>();
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();
            consumer.Consume().ReturnsForAnyArgs(consumeResult);

            var handler = NSubstitute.Substitute.For<IKafkaMessageHandler>();

            var sut = BuildSUT(queueRefs, consumer, handler);

            var tokenSource = new CancellationTokenSource(1000);
            await sut.StartAsync(tokenSource.Token);

            await handler.Received().HandleAsync(consumeResult, queueRefs, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StopAsync_should_subscribe_to_topic()
        {
            var queueRefs = new QueueReferences("lorem", "ipsum");
            var consumer = NSubstitute.Substitute.For<IConsumer<Guid, byte[]>>();

            var sut = BuildSUT(queueRefs, consumer);

            var tokenSource = new CancellationTokenSource(1000);
            await sut.StopAsync(tokenSource.Token);

            consumer.Received(1).Unsubscribe();
        }

        private static KafkaSubscriber<IMessage> BuildSUT(
            QueueReferences queueRefs, 
            IConsumer<Guid, byte[]> consumer,
            IKafkaMessageHandler messageHandler = null)
        {
            var config = new ConsumerConfig()
            {
                GroupId = "group id"
            };
            var builder = NSubstitute.Substitute.ForPartsOf<ConsumerBuilder<Guid, byte[]>>(config);
            builder.When(b => b.Build()).DoNotCallBase();
            builder.Build().Returns(consumer);

            messageHandler ??= NSubstitute.Substitute.For<IKafkaMessageHandler>();

            var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            queueReferenceFactory.Create<IMessage>().ReturnsForAnyArgs(queueRefs);

            var logger = NSubstitute.Substitute.For<ILogger<KafkaSubscriber<IMessage>>>();

            return new KafkaSubscriber<IMessage>(builder, queueReferenceFactory, messageHandler, logger);
        }
    }
}