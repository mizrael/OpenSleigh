using System;
using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class MessageParserTests
    {
        [Fact]
        public void Resolve_should_throw_when_input_null()
        {
            var decoder = NSubstitute.Substitute.For<Utils.ISerializer>();            
            var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var sut = new MessageParser(decoder, queueReferenceFactory);

            Assert.Throws<ArgumentNullException>(() => sut.Parse(null));
        }

        [Fact]
        public void Resolve_should_throw_when_headers_do_not_contain_message_type()
        {
            var decoder = NSubstitute.Substitute.For<Utils.ISerializer>();
            var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var sut = new MessageParser(decoder, queueReferenceFactory);

            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>()
            {
                Message = new Message<string,  ReadOnlyMemory<byte>>()
                {
                    Headers = new Headers()
                }
            };

            var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
            ex.Message.Should().Contain("invalid message type");
        }

        [Fact]
        public void Resolve_should_throw_when_message_type_header_does_not_match()
        {
            Type messageType = null;
            var messageTopic = "lorem";
            var decoder = NSubstitute.Substitute.For<Utils.ISerializer>();            
            var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType); 
            var sut = new MessageParser(decoder, queueReferenceFactory);

            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>()
            {
                Topic= messageTopic,
                Message = new Message<string,  ReadOnlyMemory<byte>>()
            };
            
            var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
            ex.Message.Should().Contain("invalid message type");
        }

        [Fact]
        public void Resolve_should_return_message()
        {
            var messageType = typeof(DummyMessage);
            var messageTopic = "DummyMessage";
;            var message = DummyMessage.New();
            var encodedMessage = Newtonsoft.Json.JsonConvert.SerializeObject(message);
            var messageBytes = Encoding.UTF8.GetBytes(encodedMessage);
            
            var decoder = NSubstitute.Substitute.For<Utils.ISerializer>();
            decoder.Deserialize(messageBytes, messageType).Returns(message); 
            
            var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType); 
            
            var sut = new MessageParser(decoder, queueReferenceFactory);

            var consumeResult = new ConsumeResult<string,  ReadOnlyMemory<byte>>()
            {
                Topic = messageTopic,
                Message = new Message<string,  ReadOnlyMemory<byte>>()
                {
                    Value = messageBytes
                }
            };
            var result = sut.Parse(consumeResult);
            result.Should().Be(message);
        }
    }
}
