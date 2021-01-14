using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class JsonEncoderTests
    {
        [Fact]
        public void Encode_should_throw_when_input_null()
        {
            var sut = new JsonEncoder();
            Assert.Throws<ArgumentNullException>(() => sut.Encode((object)null));
        }

        [Fact]
        public void Encode_should_return_encoded_data()
        {
            var data = new
            {
                Foo = "bar"
            }; 
            
            var sut = new JsonEncoder();
            
            var result = sut.Encode(data);
            result.Should().NotBeNull();
            result.Value.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Decode_should_decode_input()
        {
            var data = new
            {
                Foo = "bar"
            };

            var sut = new JsonEncoder();

            var encoded = sut.Encode(data);

            var decoded = sut.Decode(encoded.Value, data.GetType());

            decoded.Should().NotBeNull();
            decoded.Should().BeEquivalentTo(data);
        }
    }
}
