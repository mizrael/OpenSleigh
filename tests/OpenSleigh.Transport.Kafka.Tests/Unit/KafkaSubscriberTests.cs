using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KafkaSubscriberTests
{
    [Fact]
    public async Task Start_should_subscribe_to_topic()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string, byte[]>>();

        var sut = BuildSUT(queueRefs, consumer);

        await sut.StartAsync(CancellationToken.None);

        await Task.Delay(250);

        consumer.Received(1).Subscribe(queueRefs.TopicName);
    }

    [Fact]
    public async Task Start_should_consume_incoming_messages()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string, byte[]>>();

        var sut = BuildSUT(queueRefs, consumer);

        await sut.StartAsync(CancellationToken.None);

        await Task.Delay(250);

        consumer.ReceivedWithAnyArgs().Consume(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_should_process_incoming_messages()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumeResult = new ConsumeResult<string, byte[]>();
        var consumer = NSubstitute.Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(consumeResult);

        var handler = NSubstitute.Substitute.For<IKafkaMessageHandler>();

        var sut = BuildSUT(queueRefs, consumer, handler);

        await sut.StartAsync(CancellationToken.None);

        await Task.Delay(250);

        await handler.Received().HandleAsync(consumeResult, queueRefs, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_should_throw_if_already_started()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string, byte[]>>();

        var sut = BuildSUT(queueRefs, consumer);

        await sut.StartAsync(CancellationToken.None);
        
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stop_should_close_consumer()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string, byte[]>>();

        var sut = BuildSUT(queueRefs, consumer);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        
        await sut.StopAsync(CancellationToken.None);
        await Task.Delay(200);

        consumer.Received(1).Close();
    }

    [Fact]
    public async Task Stop_should_throw_if_not_started()
    {
        var queueRefs = new QueueReferences("lorem", "ipsum");
        var consumer = NSubstitute.Substitute.For<IConsumer<string, byte[]>>();

        var sut = BuildSUT(queueRefs, consumer);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.StopAsync(CancellationToken.None));
    }

    private static KafkaSubscriber<IMessage> BuildSUT(
        QueueReferences queueRefs,
        IConsumer<string, byte[]> consumer,
        IKafkaMessageHandler messageHandler = null)
    {
        var config = new ConsumerConfig()
        {
            GroupId = "group id"
        };
        var builder = NSubstitute.Substitute.ForPartsOf<ConsumerBuilder<string, byte[]>>(config);
        builder.When(b => b.Build()).DoNotCallBase();
        builder.Build().Returns(consumer);

        var builderFactory = NSubstitute.Substitute.For<IConsumerBuilderFactory>();
        builderFactory.Create<IMessage, string, byte[]>().Returns(builder);

        messageHandler ??= NSubstitute.Substitute.For<IKafkaMessageHandler>();

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.Create<IMessage>().ReturnsForAnyArgs(queueRefs);

        var logger = NSubstitute.Substitute.For<ILogger<KafkaSubscriber<IMessage>>>();

        return new KafkaSubscriber<IMessage>(builderFactory, queueReferenceFactory, messageHandler, logger);
    }
}
