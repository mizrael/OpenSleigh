using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KafkaPublisherExecutorTests
{
    [Fact]
    public void ctor_should_throw_when_input_invalid()
    {
        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

        Assert.Throws<ArgumentNullException>(() => new KafkaPublisherExecutor(null, logger));
        Assert.Throws<ArgumentNullException>(() => new KafkaPublisherExecutor(producer, null));
    }

    [Fact]
    public async Task PublishAsync_should_throw_when_input_invalid()
    {
        var message = DummyMessage.CreateOutboxMessage();

        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

        var sut = new KafkaPublisherExecutor(producer, logger);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(null, "lorem"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(message, null));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(message, ""));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(message, "   "));
    }

    [Fact]
    public async Task PublishAsync_should_publish_message()
    {
        var message = DummyMessage.CreateOutboxMessage();

        var topicName = "lorem";

        var producerResult = new DeliveryResult<string, byte[]>()
        {
            Status = PersistenceStatus.Persisted
        };
        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(topicName, Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(producerResult);

        var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

        var sut = new KafkaPublisherExecutor(producer, logger);

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
        var message = DummyMessage.CreateOutboxMessage();

        var topicName = "lorem";

        var producerResult = new DeliveryResult<string, byte[]>()
        {
            Status = PersistenceStatus.Persisted
        };
        var producer = NSubstitute.Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(topicName, Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(producerResult);

        var logger = NSubstitute.Substitute.For<ILogger<KafkaPublisherExecutor>>();

        var sut = new KafkaPublisherExecutor(producer, logger);

        var headers = new[]
        {
            new Header("lorem", Encoding.UTF8.GetBytes("ipsum")),
            new Header("dolor", Encoding.UTF8.GetBytes("amet"))
        };
        
        await sut.PublishAsync(message, topicName, headers);

        await producer.Received(1)
            .ProduceAsync(topicName,
                Arg.Is((Message<string, byte[]> km) => km.Headers.Count == 7 &&
                    km.Headers.Single(h => h.Key == "lorem") != null &&
                    km.Headers.Single(h => h.Key == "dolor") != null &&
                    km.Key == message.MessageId));
    }
}