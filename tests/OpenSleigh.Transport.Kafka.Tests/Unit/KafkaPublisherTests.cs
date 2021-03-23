using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using NSubstitute;
using NSubstitute.Core;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class KafkaPublisherTests
    {
        [Fact]
        public void ctor_should_throw_when_input_invalid()
        {
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();

            Assert.Throws<ArgumentNullException>(() => new KafkaPublisher(null, serializer, factory));
            Assert.Throws<ArgumentNullException>(() => new KafkaPublisher(producer, null, factory));
            Assert.Throws<ArgumentNullException>(() => new KafkaPublisher(producer, serializer, null));
        }

        [Fact]
        public async Task PublishAsync_should_throw_when_message_null()
        {
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var sut = new KafkaPublisher(producer, serializer, factory);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(null));
        }

        [Fact]
        public async Task PublishAsync_publish_message()
        {
            var message = DummyMessage.New();

            var queueRefs = new QueueReferences("lorem");

            var producerResult = new DeliveryResult<Guid, byte[]>()
            {
                Status = PersistenceStatus.Persisted
            };
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            producer.ProduceAsync(queueRefs.TopicName, Arg.Any<Message<Guid, byte[]>>(), Arg.Any<CancellationToken>())
                .Returns(producerResult);

            var serializer = NSubstitute.Substitute.For<ISerializer>();

            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            factory.Create<DummyMessage>(message)
                .Returns(queueRefs);

            var sut = new KafkaPublisher(producer, serializer, factory);

            await sut.PublishAsync(message);

            await producer.Received(1)
                .ProduceAsync(queueRefs.TopicName,
                              Arg.Is((Message<Guid, byte[]> km) => km.Headers.Any(h =>
                                                    h.Key == HeaderNames.MessageType) &&
                                                    km.Key == message.Id));
        }

        [Fact]
        public async Task PublishAsync_should_throw_when_publish_fails()
        {
            var message = DummyMessage.New();

            var queueRefs = new QueueReferences("lorem");

            var producerResult = new DeliveryResult<Guid, byte[]>()
            {
                Status = PersistenceStatus.NotPersisted
            };
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            producer.ProduceAsync(queueRefs.TopicName, Arg.Any<Message<Guid, byte[]>>(), Arg.Any<CancellationToken>())
                .Returns(producerResult);

            var serializer = NSubstitute.Substitute.For<ISerializer>();

            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            factory.Create<DummyMessage>(message)
                .Returns(queueRefs);

            var sut = new KafkaPublisher(producer, serializer, factory);

            await Assert.ThrowsAsync<ApplicationException>(async () => await sut.PublishAsync(message));
        }
    }
}
