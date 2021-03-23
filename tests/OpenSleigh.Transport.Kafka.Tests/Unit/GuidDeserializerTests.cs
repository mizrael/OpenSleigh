using System;
using FluentAssertions;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class GuidDeserializerTests
    {
        [Fact]
        public void Deserialize_should_return_valid_instance()
        {
            var expectedGuid = Guid.NewGuid();

            var data = expectedGuid.ToByteArray();
            var sut = new GuidDeserializer();
            var result = sut.Deserialize(data, false, default);
            result.Should().Be(expectedGuid);
        }
    }
}