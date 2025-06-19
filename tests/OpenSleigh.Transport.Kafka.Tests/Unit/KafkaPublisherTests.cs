using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KafkaPublisherTests
{
    private static KafkaPublisher CreateSUT(IProducer<string, byte[]>? producer = null, IQueueReferenceFactory? factory = null)
    {
        producer ??= NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        factory ??= NSubstitute.Substitute.For<IQueueReferenceFactory>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisher>>();
        var serializer = Substitute.For<Utils.ISerializer>();
        var sut = new KafkaPublisher(factory, producer, logger, serializer);
        return sut;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishAsync_should_throw_when_topic_invalid(string topicName)
    {
        var message = DummyMessage.CreateEnvelope();

        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisher>>();

        var sut = CreateSUT();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await sut.PublishAsync(message, topicName));
    }

    [Fact]
    public async Task PublishAsync_should_throw_when_message_null()
    {
        var sut = CreateSUT();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(null));
    }

    [Fact]
    public async Task PublishAsync_publish_message()
    {
        var message = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "ipsum");

        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(queueRefs.TopicName, Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryReport<string, byte[]>()
            {
                Status = PersistenceStatus.Persisted
            });

        var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        factory.Create(message).ReturnsForAnyArgs(queueRefs);

        var sut = CreateSUT(producer, factory);

        await sut.PublishAsync(message);

        await producer.Received(1)
            .ProduceAsync(queueRefs.TopicName, Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_should_throw_when_publish_fails()
    {
        var message = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "ipsum");

        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(queueRefs.TopicName, Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryReport<string, byte[]>()
            {
                Status = PersistenceStatus.NotPersisted
            });

        var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        factory.Create(message).ReturnsForAnyArgs(queueRefs);

        var sut = CreateSUT(producer, factory);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.PublishAsync(message));
    }

    [Fact]
    public async Task PublishAsync_should_publish_message()
    {
        var message = DummyMessage.CreateEnvelope();

        var topicName = "lorem";

        var producerResult = new DeliveryResult<string, byte[]>()
        {
            Status = PersistenceStatus.Persisted
        };
        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(topicName, Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(producerResult);

        var sut = CreateSUT(producer);

        await sut.PublishAsync(message, topicName);

        await producer.Received(1)
            .ProduceAsync(topicName,
                Arg.Is((Message<string, byte[]> km) =>
                    km.Key == message.MessageId &&
                    km.Headers.Any(h => h.Key == nameof(message.MessageType) && h.GetValueBytes().SequenceEqual(Encoding.UTF8.GetBytes(typeof(DummyMessage).FullName))) &&
                    km.Headers.Any(h => h.Key == nameof(message.CorrelationId) && h.GetValueBytes().SequenceEqual(Encoding.UTF8.GetBytes(message.CorrelationId))) &&
                    km.Headers.Any(h => h.Key == nameof(message.SenderId) && h.GetValueBytes().SequenceEqual(Encoding.UTF8.GetBytes(message.SenderId.ToString())))
                   ));
    }

    [Fact]
    public async Task PublishAsync_should_include_additional_headers_when_provided()
    {
        var message = DummyMessage.CreateEnvelope();

        var topicName = "lorem";

        var producerResult = new DeliveryResult<string, byte[]>()
        {
            Status = PersistenceStatus.Persisted
        };
        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(topicName, Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(producerResult);

        var sut = CreateSUT(producer);

        var headers = new[]
        {
            new Header("lorem", Encoding.UTF8.GetBytes("ipsum")),
            new Header("dolor", Encoding.UTF8.GetBytes("amet"))
        };

        await sut.PublishAsync(message, topicName, headers);

        await producer.Received(1)
            .ProduceAsync(topicName,
                Arg.Is((Message<string, byte[]> km) => km.Headers.Count == 6 &&
                    km.Headers.Single(h => h.Key == "lorem") != null &&
                    km.Headers.Single(h => h.Key == "dolor") != null &&
                    km.Key == message.MessageId));
    }
}
