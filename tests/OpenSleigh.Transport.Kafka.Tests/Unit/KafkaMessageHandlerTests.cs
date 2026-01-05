using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using OpenSleigh.Outbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KafkaMessageHandlerTests
{
    [Fact]
    public async Task StartAsync_should_return_false_when_message_null()
    {
        var parser = NSubstitute.Substitute.For<IMessageParser>();
        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();

        var queueRefs = new QueueReferences("lorem", "ipsum");
        queueRefFactory.Get(Arg.Any<string>()).Returns(queueRefs);

        var consumeResult = new ConsumeResult<string, byte[]>();

        var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo, queueRefFactory);

        var result = await sut.HandleAsync(consumeResult);
        Assert.False(result);

        parser.Received().Parse(consumeResult);

        await messageProcessor.DidNotReceiveWithAnyArgs()
                            .ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_should_process_incoming_messages()
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var expectedMessage = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "ipsum");

        var parser = NSubstitute.Substitute.For<IMessageParser>();
        parser.Parse(consumeResult)
            .Returns(expectedMessage);

        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Get(Arg.Any<string>()).Returns(queueRefs);

        var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo, queueRefFactory);

        var result = await sut.HandleAsync(consumeResult);
        Assert.True(result);

        await messageProcessor.Received().ProcessAsync((dynamic)expectedMessage, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_should_republish_to_deadletter_when_exception_occurs()
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var expectedMessage = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "ipsum");

        var parser = NSubstitute.Substitute.For<IMessageParser>();
        parser.Parse(consumeResult)
            .Returns(expectedMessage);

        var ex = new Exception("whoops");
        var expectedErrorHeader = new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message));
        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        messageProcessor.WhenForAnyArgs(mp => ((ValueTask)mp.ProcessAsync((dynamic)expectedMessage)).AsTask())
            .Throw(ex);

        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();

        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Get(Arg.Any<string>()).Returns(queueRefs);

        var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo, queueRefFactory);

        await sut.HandleAsync(consumeResult);

        await publisher.Received(1)
            .PublishAsync(
            expectedMessage,
            queueRefs.DeadLetterTopicName,
            Arg.Is((IEnumerable<Header> headers) => headers.Any(h => h.Key == expectedErrorHeader.Key)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_should_not_republish_to_deadletter_when_exception_occurs_and_no_deadletter_available()
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var expectedMessage = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "");

        var parser = NSubstitute.Substitute.For<IMessageParser>();
        parser.Parse(consumeResult)
              .Returns(expectedMessage);

        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        messageProcessor.WhenForAnyArgs(mp => ((ValueTask)mp.ProcessAsync((dynamic)expectedMessage)).AsTask())
            .Throw(new Exception("whoops"));

        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();

        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Get(Arg.Any<string>()).Returns(queueRefs);

        var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo, queueRefFactory);

        await sut.HandleAsync(consumeResult);

        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<MessageEnvelope>(),
            Arg.Any<string>(),
            null,
            Arg.Any<CancellationToken>());
    }
}