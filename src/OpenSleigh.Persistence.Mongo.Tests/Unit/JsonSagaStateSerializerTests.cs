using FluentAssertions;
using OpenSleigh.Core;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit
{
    public class JsonSagaStateSerializerTests
    {
        [Fact]
        public async Task SerializeAsync_should_serialize_state()
        {
            var state = new DummyState(Guid.NewGuid(), "foo", 42);
            var sut = new JsonSagaStateSerializer();
            var serialized = await sut.SerializeAsync(state);
            serialized.Should().NotBeNull();

            var deserializedState = System.Text.Json.JsonSerializer.Deserialize<DummyState>(serialized);
            deserializedState.Should().NotBeNull();
            deserializedState.Id.Should().Be(state.Id);
            deserializedState.Bar.Should().Be(state.Bar);
            deserializedState.Foo.Should().Be(state.Foo);
        }

        [Fact]
        public async Task SerializeAsync_should_serialize_outbox()
        {
            var state = new DummyState(Guid.NewGuid(), "foo", 42);
            state.AddToOutbox(DummyMessage.New());
            state.AddToOutbox(DummyMessage.New());
            state.AddToOutbox(DummyMessage.New());

            var sut = new JsonSagaStateSerializer();
            var serialized = await sut.SerializeAsync(state);
            serialized.Should().NotBeNull();

            var deserializedState = await sut.DeserializeAsync<DummyState>(serialized);
            deserializedState.Should().NotBeNull();
            deserializedState.Outbox.Should().NotBeNullOrEmpty()
                .And.HaveCount(3);
        }
    }

    public record DummyMessage(Guid Id) : IMessage
    {
        public Guid CorrelationId => this.Id;
        public static DummyMessage New() => new DummyMessage(Guid.NewGuid());
    }
}
