using FluentAssertions;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class JsonSerializerTests
    {
        private readonly static Guid correlationId = Guid.Parse("e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa");
        private readonly static Guid msg1Id = Guid.Parse("5ce5d649-922e-4a60-8fe3-0cde28f5fdb8");
        private readonly static Guid msg2Id = Guid.Parse("7835b785-eb8c-4e88-8330-bbe94bd8172c");
        private const string jsonString = "{\"Foo\":\"foo\",\"Bar\":42,\"Id\":\"e846ad99-ddb5-4ed1-b92e-3da6e2bf78fa\",\"IsCompleted\":true,\"ProcessedMessagesIds\":[\"5ce5d649-922e-4a60-8fe3-0cde28f5fdb8\",\"7835b785-eb8c-4e88-8330-bbe94bd8172c\"]}";

        [Fact]
        public void Serialize_should_serialize_object()
        {
            var msg1 = new DummyMessage(Id: msg1Id, correlationId);
            var msg2 = new DummyMessage(msg2Id, correlationId);

            var state = new DummyState(correlationId, "foo", 42);
            state.MarkAsCompleted();
            state.SetAsProcessed(msg1);
            state.SetAsProcessed(msg2);

            var sut = new JsonSerializer();
            var serialized = sut.Serialize(state);
            serialized.Should().NotBeNull();

            var jsonString = Encoding.UTF8.GetString(serialized);
            jsonString.Should().Be(jsonString);

            var deserializedState = System.Text.Json.JsonSerializer.Deserialize<DummyState>(serialized);
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(state.Id);
            deserializedState.Bar.Should().Be(state.Bar);
            deserializedState.Foo.Should().Be(state.Foo);
            deserializedState.ProcessedMessagesIds.Should().HaveCount(2)
                .And.Contain(msg1Id)
                .And.Contain(msg2Id);
        }

        [Fact]
        public async Task DeserializeAsync_should_deserialize_state()
        {
            
            var data = Encoding.UTF8.GetBytes(jsonString);
            using var ms = new MemoryStream(data);

            var sut = new JsonSerializer();
            var deserializedState = await sut.DeserializeAsync<DummyState>(ms);
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(correlationId);
            deserializedState.Bar.Should().Be(42);
            deserializedState.Foo.Should().Be("foo");

            deserializedState.ProcessedMessagesIds.Should().HaveCount(2)
                .And.Contain(msg1Id)
                .And.Contain(msg2Id);
        }

        [Fact]
        public void Deserialize_should_deserialize_state()
        {
            var data = Encoding.UTF8.GetBytes(jsonString);
            
            var sut = new JsonSerializer();
            var deserializedState = sut.Deserialize<DummyState>(data);
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(correlationId);
            deserializedState.Bar.Should().Be(42);
            deserializedState.Foo.Should().Be("foo");

            deserializedState.ProcessedMessagesIds.Should().HaveCount(2)
                .And.Contain(msg1Id)
                .And.Contain(msg2Id);
        }

        [Fact]
        public void Deserialize_to_object_should_deserialize_state()
        {
            var data = Encoding.UTF8.GetBytes(jsonString);

            var sut = new JsonSerializer();
            var deserializedState = sut.Deserialize(data, typeof(DummyState)) as DummyState;
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(correlationId);
            deserializedState.Bar.Should().Be(42);
            deserializedState.Foo.Should().Be("foo");

            deserializedState.ProcessedMessagesIds.Should().HaveCount(2)
                .And.Contain(msg1Id)
                .And.Contain(msg2Id);
        }
    }
}
