using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KafkaSubscriberTests
{
    [Fact]
    public void Start_should_subscribe_to_topic()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string,  ReadOnlyMemory<byte>>>();

        var sut = BuildSUT(queueRefs, consumer);
        
        consumer.Received(1).Subscribe(queueRefs.TopicName);
    }

    [Fact]
    public async Task Start_should_consume_incoming_messages()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string,  ReadOnlyMemory<byte>>>();

        var sut = BuildSUT(queueRefs, consumer);

        sut.Start();

        await Task.Delay(250);

        consumer.ReceivedWithAnyArgs().Consume(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_should_process_incoming_messages()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>();
        var consumer = NSubstitute.Substitute.For<IConsumer<string,  ReadOnlyMemory<byte>>>();
        consumer.Consume(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(consumeResult);

        var handler = NSubstitute.Substitute.For<IKafkaMessageHandler>();

        var sut = BuildSUT(queueRefs, consumer, handler);

        sut.Start();

        await Task.Delay(250);

        await handler.Received().HandleAsync(consumeResult, queueRefs, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_should_close_consumer()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string,  ReadOnlyMemory<byte>>>();

        var sut = BuildSUT(queueRefs, consumer);

        sut.Start();
        await Task.Delay(200);
        sut.Stop();

        consumer.Received(1).Close();
    }

    private static KafkaSubscriber<IMessage> BuildSUT(
        QueueReferences queueRefs, 
        IConsumer<string,  ReadOnlyMemory<byte>> consumer,
        IKafkaMessageHandler messageHandler = null)
    {
        var config = new ConsumerConfig()
        {
            GroupId = "group id"
        };
        var builder = NSubstitute.Substitute.ForPartsOf<ConsumerBuilder<string, ReadOnlyMemory<byte>>>(config);
        builder.When(b => b.Build()).DoNotCallBase();
        builder.Build().Returns(consumer);

        var builderFactory = NSubstitute.Substitute.For<IConsumerBuilderFactory>();
        builderFactory.Create<IMessage, string,  ReadOnlyMemory<byte>>().Returns(builder);

        messageHandler ??= NSubstitute.Substitute.For<IKafkaMessageHandler>();

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.Create<IMessage>().ReturnsForAnyArgs(queueRefs);

        var logger = NSubstitute.Substitute.For<ILogger<KafkaSubscriber<IMessage>>>();

        return new KafkaSubscriber<IMessage>(builderFactory, queueReferenceFactory, messageHandler, logger);
    }
}
