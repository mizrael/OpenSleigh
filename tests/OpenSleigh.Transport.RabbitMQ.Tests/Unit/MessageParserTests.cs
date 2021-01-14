using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using OpenSleigh.Core;
using RabbitMQ.Client;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class MessageParserTests
    {
        [Fact]
        public void Resolve_should_throw_when_basicProperties_null()
        {
            var decoder = NSubstitute.Substitute.For<IDecoder>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new MessageParser(decoder, resolver);

            Assert.Throws<ArgumentNullException>(() => sut.Resolve(null, null));
        }

        [Fact]
        public void Resolve_should_throw_when_basicProperties_headers_null()
        {
            var decoder = NSubstitute.Substitute.For<IDecoder>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new MessageParser(decoder, resolver);
            
            var basicProperties = NSubstitute.Substitute.For<IBasicProperties>();
            basicProperties.Headers.ReturnsNull();
            
            var ex = Assert.Throws<ArgumentNullException>(() => sut.Resolve(basicProperties, null));
            ex.Message.Should().Contain("message headers are missing");
        }


        [Fact]
        public void Resolve_should_throw_when_basicProperties_headers_do_not_contain_message_type()
        {
            var decoder = NSubstitute.Substitute.For<IDecoder>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new MessageParser(decoder, resolver);

            var basicProperties = NSubstitute.Substitute.For<IBasicProperties>();
            
            var ex = Assert.Throws<ArgumentException>(() => sut.Resolve(basicProperties, null));
            ex.Message.Should().Contain("invalid message type");
        }

        [Fact]
        public void Resolve_should_throw_when_message_type_header_does_not_match()
        {
            var decoder = NSubstitute.Substitute.For<IDecoder>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new MessageParser(decoder, resolver);

            var basicProperties = NSubstitute.Substitute.For<IBasicProperties>();

            var invalidType = typeof(MessageParserTests);
            var invalidTypeBytes = Encoding.UTF8.GetBytes(invalidType.FullName);
            var headers = new Dictionary<string, object>()
            {
                {HeaderNames.MessageType, invalidTypeBytes}
            };
            basicProperties.Headers.Returns(headers);
            
            var ex = Assert.Throws<ArgumentException>(() => sut.Resolve(basicProperties, null));
            ex.Message.Should().Contain("message has the wrong type");
        }

        [Fact]
        public void Resolve_should_return_message()
        {
            var decoder = NSubstitute.Substitute.For<IDecoder>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new MessageParser(decoder, resolver);

            var basicProperties = NSubstitute.Substitute.For<IBasicProperties>();

            var messageType = typeof(DummyMessage);
            var headers = new Dictionary<string, object>()
            {
                {HeaderNames.MessageType, Encoding.UTF8.GetBytes(messageType.FullName)}
            };
            basicProperties.Headers.Returns(headers);

            var message = DummyMessage.New();
            var encodedMessage = Newtonsoft.Json.JsonConvert.SerializeObject(message);
            var messageBytes = Encoding.UTF8.GetBytes(encodedMessage);
            decoder.Decode(messageBytes, messageType).Returns(message);

            resolver.Resolve(messageType.FullName).Returns(messageType);

            var result = sut.Resolve(basicProperties, messageBytes);
            result.Should().Be(message);
        }
    }
}
