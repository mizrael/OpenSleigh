using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class KafkaMessageHandlerTests
    {

        [Fact]
        public async Task StartAsync_should_parse_incoming_messages()
        {
            var parser = NSubstitute.Substitute.For<IMessageParser>();
            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
            var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
            
            var queueRefs = new QueueReferences("lorem", "ipsum");
            
            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>();

            var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo);

            await sut.HandleAsync(consumeResult, queueRefs);

            parser.Received().Parse(consumeResult);
        }

        [Fact]
        public async Task StartAsync_should_process_incoming_messages()
        {
            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>();
            var expectedMessage = NSubstitute.Substitute.For<IMessage>();
            var queueRefs = new QueueReferences("lorem", "ipsum");

            var parser = NSubstitute.Substitute.For<IMessageParser>();
            parser.Parse(consumeResult)
                .Returns(expectedMessage);

            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
            var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

            var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo);

            await sut.HandleAsync(consumeResult, queueRefs);

            await messageProcessor.Received().ProcessAsync((dynamic)expectedMessage, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_republish_to_deadletter_when_exception_occurs()
        {
            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>();
            var expectedMessage = NSubstitute.Substitute.For<IMessage>();
            var queueRefs = new QueueReferences("lorem", "ipsum");

            var parser = NSubstitute.Substitute.For<IMessageParser>();
            parser.Parse(consumeResult)
                .Returns(expectedMessage);

            var ex = new Exception("whoops");
            var expectedErrorHeader = new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message));
            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
            messageProcessor.WhenForAnyArgs(mp => mp.ProcessAsync((dynamic)expectedMessage))
                .Throw(ex);

            var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
            
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

            var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo);

            await sut.HandleAsync(consumeResult, queueRefs);

            await publisher.Received().PublishAsync(expectedMessage, queueRefs.DeadLetterTopicName,
                Arg.Is((Header[] headers) => headers.Any(h => h.Key == expectedErrorHeader.Key)),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_not_republish_to_deadletter_when_exception_occurs_and_no_deadletter_available()
        {
            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>();
            var expectedMessage = NSubstitute.Substitute.For<IMessage>();
            var queueRefs = new QueueReferences("lorem", "");

            var parser = NSubstitute.Substitute.For<IMessageParser>();
            parser.Parse(consumeResult)
                .Returns(expectedMessage);

            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
            messageProcessor.WhenForAnyArgs(mp => mp.ProcessAsync((dynamic)expectedMessage))
                .Throw(new Exception("whoops"));

            var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
            
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

            var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo);

            await sut.HandleAsync(consumeResult, queueRefs);

            await publisher.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<IMessage>(),
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_hanle_null_messages()
        {
            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>();
            var queueRefs = new QueueReferences("lorem", "ipsum");

            var parser = NSubstitute.Substitute.For<IMessageParser>();
            parser.Parse(consumeResult)
                .ReturnsNull();

            var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();

            var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
            var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
            
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

            var sut = new KafkaMessageHandler(parser, messageProcessor, publisher, logger, sysInfo);

            await sut.HandleAsync(consumeResult, queueRefs);

            await messageProcessor.DidNotReceiveWithAnyArgs()
                                .ProcessAsync(Arg.Any<IMessage>(), Arg.Any<CancellationToken>());
        }
    }
}