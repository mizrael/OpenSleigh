using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class KafkaPublisherTests
    {
        [Fact]
        public void ctor_should_throw_when_input_invalid()
        {
            var executor = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            
            Assert.Throws<ArgumentNullException>(() => new KafkaPublisher(null, factory));
            Assert.Throws<ArgumentNullException>(() => new KafkaPublisher(executor, null));
        }

        [Fact]
        public async Task PublishAsync_should_throw_when_message_null()
        {
            var executor = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var sut = new KafkaPublisher(executor, factory);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(null));
        }

        [Fact]
        public async Task PublishAsync_publish_message()
        {
            var message = DummyMessage.New();
            var queueRefs = new QueueReferences("lorem", "ipsum");

            var executor = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            executor.PublishAsync(message, queueRefs.TopicName, null, Arg.Any<CancellationToken>())
                .Returns(new DeliveryReport<string,  ReadOnlyMemory<byte>>()
                {
                    Status = PersistenceStatus.Persisted
                });

            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            factory.Create(message).ReturnsForAnyArgs(queueRefs);

            var sut = new KafkaPublisher(executor, factory);

            await sut.PublishAsync(message);

            await executor.Received(1)
                .PublishAsync(message, queueRefs.TopicName);
        }

        [Fact]
        public async Task PublishAsync_should_throw_when_publish_fails()
        {
            var message = DummyMessage.New();
            var queueRefs = new QueueReferences("lorem", "ipsum");

            var executor = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            executor.PublishAsync(message, queueRefs.TopicName, null, Arg.Any<CancellationToken>())
                .Returns(new DeliveryReport<string,  ReadOnlyMemory<byte>>()
                {
                    Status = PersistenceStatus.NotPersisted
                });

            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            factory.Create(message).ReturnsForAnyArgs(queueRefs);

            var sut = new KafkaPublisher(executor, factory);
            
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.PublishAsync(message));
        }
    }
}
