using FluentAssertions;
using System;
using System.Threading.Tasks;
using OpenSleigh.Persistence.Mongo.Utils;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit
{
    public class JsonSerializerTests
    {
        [Fact]
        public async Task SerializeAsync_should_serialize_object()
        {
            var state = new DummyState(Guid.NewGuid(), "foo", 42);
            var sut = new JsonSerializer();
            var serialized = await sut.SerializeAsync(state);
            serialized.Should().NotBeNull();

            var deserializedState = System.Text.Json.JsonSerializer.Deserialize<DummyState>(serialized);
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(state.Id);
            deserializedState.Bar.Should().Be(state.Bar);
            deserializedState.Foo.Should().Be(state.Foo);
        }
    }
}
