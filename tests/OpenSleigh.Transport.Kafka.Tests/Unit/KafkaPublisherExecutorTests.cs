using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class KafkaPublisherExecutorTests
    {
        [Fact]
        public void ctor_should_throw_when_input_invalid()
        {
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

            Assert.Throws<ArgumentNullException>(() => new KafkaPublisherExecutor(null, serializer, logger));
            Assert.Throws<ArgumentNullException>(() => new KafkaPublisherExecutor(producer, null, logger));
            Assert.Throws<ArgumentNullException>(() => new KafkaPublisherExecutor(producer, serializer, null));
        }

        [Fact]
        public async Task PublishAsync_should_throw_when_input_invalid()
        {
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

            var sut = new KafkaPublisherExecutor(producer, serializer, logger);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(null, "lorem"));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(DummyMessage.New(), null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(DummyMessage.New(), ""));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(DummyMessage.New(), "   "));
        }

        [Fact]
        public async Task PublishAsync_should_publish_message()
        {
            var message = DummyMessage.New();

            var topicName = "lorem";

            var producerResult = new DeliveryResult<Guid, byte[]>()
            {
                Status = PersistenceStatus.Persisted
            };
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            producer.ProduceAsync(topicName, Arg.Any<Message<Guid, byte[]>>(), Arg.Any<CancellationToken>())
                .Returns(producerResult);

            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

            var sut = new KafkaPublisherExecutor(producer, serializer, logger);

            await sut.PublishAsync(message, topicName);

            await producer.Received(1)
                .ProduceAsync(topicName,
                    Arg.Is((Message<Guid, byte[]> km) => km.Headers.Any(h =>
                                                             h.Key == HeaderNames.MessageType) &&
                                                         km.Key == message.Id));
        }

        [Fact]
        public async Task PublishAsync_should_include_additional_headers_when_provided()
        {
            var message = DummyMessage.New();

            var topicName = "lorem";

            var producerResult = new DeliveryResult<Guid, byte[]>()
            {
                Status = PersistenceStatus.Persisted
            };
            var producer = NSubstitute.Substitute.For<IProducer<Guid, byte[]>>();
            producer.ProduceAsync(topicName, Arg.Any<Message<Guid, byte[]>>(), Arg.Any<CancellationToken>())
                .Returns(producerResult);

            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

            var sut = new KafkaPublisherExecutor(producer, serializer, logger);

            var headers = new[]
            {
                new Header("lorem", Encoding.UTF8.GetBytes("ipsum")),
                new Header("dolor", Encoding.UTF8.GetBytes("amet"))
            };
            
            await sut.PublishAsync(message, topicName, headers);

            await producer.Received(1)
                .ProduceAsync(topicName,
                    Arg.Is((Message<Guid, byte[]> km) => km.Headers.Count == 3 &&
                        km.Headers.Single(h => h.Key == HeaderNames.MessageType) != null &&
                        km.Headers.Single(h => h.Key == "lorem") != null &&
                        km.Headers.Single(h => h.Key == "dolor") != null &&
                        km.Key == message.Id));
        }
    }
}