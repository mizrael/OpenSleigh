using System;
using System.Text;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public class MessageParserTests
    {
        [Fact]
        public void ctor_should_throw_when_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => new MessageParser(null));
        }

        [Fact]
        public void Resolve_should_throw_when_input_null()
        {
            var encoder = NSubstitute.Substitute.For<ISerializer>();
            var sut = new MessageParser(encoder);
            Assert.Throws<ArgumentNullException>(() => sut.Resolve<DummyMessage>(null));
        }

        [Fact]
        public void Resolve_should_deserialize_message()
        {
            var expectedMessage = DummyMessage.New();
            var messageJson = Newtonsoft.Json.JsonConvert.SerializeObject(expectedMessage);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);
            var messageData = new BinaryData(messageBytes);
            
            var encoder = NSubstitute.Substitute.For<ISerializer>();
            encoder.Deserialize(messageData, typeof(DummyMessage))
                .Returns(expectedMessage);
            
            var sut = new MessageParser(encoder);

            var result = sut.Resolve<DummyMessage>(messageData);

            result.Should().Be(expectedMessage);
            encoder.Received(1)
                .Deserialize(messageData, typeof(DummyMessage));
        }
    }
}