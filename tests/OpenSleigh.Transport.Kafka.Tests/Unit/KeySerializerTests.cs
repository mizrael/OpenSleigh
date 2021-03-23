using System;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class KeySerializerTests
    {
        [Fact]
        public void Serialize_should_return_bytes_from_guid()
        {
            var sut = new KeySerializer<Guid>();

            var expectedGuid = Guid.NewGuid();
            var result = sut.Serialize(expectedGuid, default);
            result.Should().BeEquivalentTo(expectedGuid.ToByteArray());
        }

        [Fact]
        public void Serialize_should_return_json_bytes_from_class()
        {
            var sut = new KeySerializer<DummyMessage>();

            var message = DummyMessage.New();
            var jsonMessage = JsonSerializer.Serialize(message);
            var expectedData = Encoding.UTF8.GetBytes(jsonMessage); 
            
            var result = sut.Serialize(message, default);
            result.Should().BeEquivalentTo(expectedData);
        }
    }
}