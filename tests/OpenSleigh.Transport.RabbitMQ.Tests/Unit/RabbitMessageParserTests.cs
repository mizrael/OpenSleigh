using FluentAssertions;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

public class RabbitMessageParserTests
{
    [Fact]
    public void ctor_should_throw_if_typeResolver_is_null()
    {
        var serializer = new FakeSerializer();

        Assert.Throws<ArgumentNullException>(() => new RabbitMessageParser(null!, serializer));
    }

    [Fact]
    public void ctor_should_throw_if_serializer_is_null()
    {
        var typeResolver = Substitute.For<ITypeResolver>();

        Assert.Throws<ArgumentNullException>(() => new RabbitMessageParser(typeResolver, null!));
    }

    [Fact]
    public async Task ParseMessageAsync_should_return_message_envelope_when_valid()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(Arg.Any<string>(), throwOnError: true).Returns(typeof(FakeSagaStarter));

        var serializer = new FakeSerializer { DeserializeResult = new FakeSagaStarter() };

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var messageId = Guid.NewGuid().ToString();
        var correlationId = Guid.NewGuid().ToString();
        var senderId = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;

        var eventArgs = CreateBasicDeliverEventArgs(messageId, correlationId, senderId, createdAt, typeof(FakeSagaStarter).AssemblyQualifiedName!);

        var result = await sut.ParseMessageAsync(eventArgs);

        result.Should().NotBeNull();
        result!.MessageId.Should().Be(messageId);
        result.CorrelationId.Should().Be(correlationId);
        result.SenderId.Should().Be(senderId);
        result.MessageType.Should().Be(typeof(FakeSagaStarter));
    }

    [Fact]
    public async Task ParseMessageAsync_should_throw_when_messageId_is_null()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var serializer = new FakeSerializer();

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var eventArgs = CreateBasicDeliverEventArgs(
            messageId: null,
            correlationId: Guid.NewGuid().ToString(),
            senderId: Guid.NewGuid().ToString(),
            createdAt: DateTimeOffset.UtcNow,
            messageTypeName: typeof(FakeSagaStarter).AssemblyQualifiedName!);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ParseMessageAsync(eventArgs));
    }

    [Fact]
    public async Task ParseMessageAsync_should_throw_when_correlationId_is_null()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var serializer = new FakeSerializer();

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var eventArgs = CreateBasicDeliverEventArgs(
            messageId: Guid.NewGuid().ToString(),
            correlationId: null,
            senderId: Guid.NewGuid().ToString(),
            createdAt: DateTimeOffset.UtcNow,
            messageTypeName: typeof(FakeSagaStarter).AssemblyQualifiedName!);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ParseMessageAsync(eventArgs));
    }

    [Fact]
    public async Task ParseMessageAsync_should_throw_when_messageType_header_is_missing()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        var serializer = new FakeSerializer();

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var eventArgs = CreateBasicDeliverEventArgs(
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            senderId: Guid.NewGuid().ToString(),
            createdAt: DateTimeOffset.UtcNow,
            messageTypeName: null);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.ParseMessageAsync(eventArgs));
    }

    [Fact]
    public async Task ParseMessageAsync_should_throw_when_senderId_header_is_missing()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(Arg.Any<string>(), throwOnError: true).Returns(typeof(FakeSagaStarter));

        var serializer = new FakeSerializer();

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var eventArgs = CreateBasicDeliverEventArgs(
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            senderId: null,
            createdAt: DateTimeOffset.UtcNow,
            messageTypeName: typeof(FakeSagaStarter).AssemblyQualifiedName!);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.ParseMessageAsync(eventArgs));
    }

    [Fact]
    public async Task ParseMessageAsync_should_throw_when_createdAt_header_is_invalid()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(Arg.Any<string>(), throwOnError: true).Returns(typeof(FakeSagaStarter));

        var serializer = new FakeSerializer();

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var headers = new Dictionary<string, object?>
        {
            [nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes(typeof(FakeSagaStarter).AssemblyQualifiedName!),
            [nameof(MessageEnvelope.SenderId)] = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()),
            [nameof(MessageEnvelope.CreatedAt)] = Encoding.UTF8.GetBytes("invalid-date")
        };

        var basicProperties = Substitute.For<IReadOnlyBasicProperties>();
        basicProperties.MessageId.Returns(Guid.NewGuid().ToString());
        basicProperties.CorrelationId.Returns(Guid.NewGuid().ToString());
        basicProperties.Headers.Returns(headers);

        var eventArgs = new BasicDeliverEventArgs(
            consumerTag: "test-consumer",
            deliveryTag: 1,
            redelivered: false,
            exchange: "test-exchange",
            routingKey: "test-routing-key",
            properties: basicProperties,
            body: new ReadOnlyMemory<byte>([1, 2, 3]));

        await Assert.ThrowsAsync<FormatException>(() => sut.ParseMessageAsync(eventArgs));
    }

    [Fact]
    public async Task ParseMessageAsync_should_throw_when_type_cannot_be_resolved()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(Arg.Any<string>(), throwOnError: true).Returns(x => throw new TypeLoadException("Type not found"));

        var serializer = new FakeSerializer();

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var eventArgs = CreateBasicDeliverEventArgs(
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            senderId: Guid.NewGuid().ToString(),
            createdAt: DateTimeOffset.UtcNow,
            messageTypeName: "NonExistent.Type, NonExistent.Assembly");

        await Assert.ThrowsAsync<TypeLoadException>(() => sut.ParseMessageAsync(eventArgs));
    }

    [Fact]
    public async Task ParseMessageAsync_should_throw_when_deserialization_fails()
    {
        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(Arg.Any<string>(), throwOnError: true).Returns(typeof(FakeSagaStarter));

        var serializer = new FakeSerializer { DeserializeResult = null };

        var sut = new RabbitMessageParser(typeResolver, serializer);

        var eventArgs = CreateBasicDeliverEventArgs(
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString(),
            senderId: Guid.NewGuid().ToString(),
            createdAt: DateTimeOffset.UtcNow,
            messageTypeName: typeof(FakeSagaStarter).AssemblyQualifiedName!);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.ParseMessageAsync(eventArgs));
    }

    private static BasicDeliverEventArgs CreateBasicDeliverEventArgs(
        string? messageId,
        string? correlationId,
        string? senderId,
        DateTimeOffset createdAt,
        string? messageTypeName)
    {
        var headers = new Dictionary<string, object?>();

        if (messageTypeName is not null)
            headers[nameof(MessageEnvelope.MessageType)] = Encoding.UTF8.GetBytes(messageTypeName);

        if (senderId is not null)
            headers[nameof(MessageEnvelope.SenderId)] = Encoding.UTF8.GetBytes(senderId);

        headers[nameof(MessageEnvelope.CreatedAt)] = Encoding.UTF8.GetBytes(createdAt.ToString("O"));

        var basicProperties = Substitute.For<IReadOnlyBasicProperties>();
        basicProperties.MessageId.Returns(messageId);
        basicProperties.CorrelationId.Returns(correlationId);
        basicProperties.Headers.Returns(headers);

        return new BasicDeliverEventArgs(
            consumerTag: "test-consumer",
            deliveryTag: 1,
            redelivered: false,
            exchange: "test-exchange",
            routingKey: "test-routing-key",
            properties: basicProperties,
            body: new ReadOnlyMemory<byte>([1, 2, 3]));
    }

    private class FakeSerializer : ISerializer
    {
        public object? DeserializeResult { get; set; }

        public byte[] Serialize(object data) => [];

        public object? Deserialize(ReadOnlySpan<byte> data, Type returnType) => DeserializeResult;
    }
}
