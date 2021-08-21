using FluentAssertions;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class JsonSerializerTests
    {
        [Fact]
        public async Task SerializeAsync_should_serialize_object()
        {
            var state = new DummyState(Guid.Parse("e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa"), "foo", 42);
            var sut = new JsonSerializer();
            var serialized = await sut.SerializeAsync(state);
            serialized.Should().NotBeNull();

            var jsonString = Encoding.UTF8.GetString(serialized.Span);
            jsonString.Should()
                .Be(
                    "{\"$type\":\"OpenSleigh.Core.Tests.Sagas.DummyState, OpenSleigh.Core.Tests\",\"_processedMessages\":{\"$type\":\"System.Collections.Generic.Dictionary`2[[System.Guid, System.Private.CoreLib],[OpenSleigh.Core.Messaging.IMessage, OpenSleigh.Core]], System.Private.CoreLib\"},\"_isComplete\":false,\"Foo\":\"foo\",\"Bar\":42,\"Id\":\"e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa\"}");


            var deserializedState = System.Text.Json.JsonSerializer.Deserialize<DummyState>(serialized.Span);
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(state.Id);
            deserializedState.Bar.Should().Be(state.Bar);
            deserializedState.Foo.Should().Be(state.Foo);
        }

        [Fact]
        public async Task DeserializeAsync_should_deserialize_state()
        {
            var jsonString =
                "{\"$type\":\"OpenSleigh.Core.Tests.Sagas.DummyState, OpenSleigh.Core.Tests\",\"_processedMessages\":{\"$type\":\"System.Collections.Generic.Dictionary`2[[System.Guid, System.Private.CoreLib],[OpenSleigh.Core.Messaging.IMessage, OpenSleigh.Core]], System.Private.CoreLib\"},\"_isComplete\":false,\"Foo\":\"foo\",\"Bar\":42,\"Id\":\"e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa\"}";
            var data = Encoding.UTF8.GetBytes(jsonString);

            var sut = new JsonSerializer();
            var deserializedState = await sut.DeserializeAsync<DummyState>(data);
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(Guid.Parse("e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa"));
            deserializedState.Bar.Should().Be(42);
            deserializedState.Foo.Should().Be("foo");
        }

        [Fact]
        public void Deserialize_should_deserialize_state()
        {
            var jsonString =
                "{\"$type\":\"OpenSleigh.Core.Tests.Sagas.DummyState, OpenSleigh.Core.Tests\",\"_processedMessages\":{\"$type\":\"System.Collections.Generic.Dictionary`2[[System.Guid, System.Private.CoreLib],[OpenSleigh.Core.Messaging.IMessage, OpenSleigh.Core]], System.Private.CoreLib\"},\"_isComplete\":false,\"Foo\":\"foo\",\"Bar\":42,\"Id\":\"e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa\"}";
            var data = Encoding.UTF8.GetBytes(jsonString);

            var sut = new JsonSerializer();
            var deserializedState = sut.Deserialize(data, typeof(DummyState)) as DummyState;
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(Guid.Parse("e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa"));
            deserializedState.Bar.Should().Be(42);
            deserializedState.Foo.Should().Be("foo");
        }
    }
}
