using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Outbox;

public class MessageEnvelopeTests 
{
    [Fact]
    public void TryCreate_ShouldReturnTrue_WhenAllParametersValid()
    {
        // Arrange
        var message = new DummyMessage();
        
        var messageType = typeof(DummyMessage);
        var body = new byte[] { 1, 2, 3 };
        var messageId = Guid.NewGuid().ToString();
        var correlationId = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        var senderId = "test-sender";

        var serializer = new MockSerializer(message);

        // Act
        var result = MessageEnvelope.TryCreate(
            body,
            messageId,
            correlationId,
            createdAt,
            messageType,
            senderId,
            serializer,
            out var envelope);

        // Assert
        Assert.True(result);
        Assert.NotNull(envelope);
        Assert.Equal(message, envelope.Message);
        Assert.Equal(messageId, envelope.MessageId);
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.Equal(createdAt, envelope.CreatedAt);
        Assert.Equal(senderId, envelope.SenderId);
    }

    [Fact]
    public void TryCreate_ShouldThrowArgumentNullException_WhenSerializerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MessageEnvelope.TryCreate(
            [1, 2, 3],
            "messageId",
            "correlationId",
            DateTimeOffset.UtcNow,
            typeof(DummyMessage),
            "senderId",
            null!,
            out _));
    }

    [Theory]
    [InlineData(null, "correlationId", "senderId")]
    [InlineData("", "correlationId", "senderId")]
    [InlineData("messageId", null, "senderId")]
    [InlineData("messageId", "", "senderId")]
    [InlineData("messageId", "correlationId", null)]
    [InlineData("messageId", "correlationId", "")]
    public void TryCreate_ShouldReturnFalse_WhenRequiredStringParametersAreNullOrEmpty(
        string messageId, string correlationId, string senderId)
    {
        // Arrange
        var serializer = Substitute.For<ISerializer>();

        // Act
        var result = MessageEnvelope.TryCreate(
            [1, 2, 3],
            messageId,
            correlationId,
            DateTimeOffset.UtcNow,
            typeof(DummyMessage),
            senderId,
            serializer,
            out var envelope);

        // Assert
        Assert.False(result);
        Assert.Null(envelope);
    }

    [Fact]
    public void TryCreate_ShouldReturnFalse_WhenBodyIsEmpty()
    {
        // Arrange
        var serializer = Substitute.For<ISerializer>();

        // Act
        var result = MessageEnvelope.TryCreate(
            Array.Empty<byte>(),
            "messageId",
            "correlationId",
            DateTimeOffset.UtcNow,
            typeof(DummyMessage),
            "senderId",
            serializer,
            out var envelope);

        // Assert
        Assert.False(result);
        Assert.Null(envelope);
    }

    [Fact]
    public void TryCreate_ShouldReturnFalse_WhenCreatedAtIsDefault()
    {
        // Arrange
        var serializer = Substitute.For<ISerializer>();

        // Act
        var result = MessageEnvelope.TryCreate(
            [1, 2, 3],
            "messageId",
            "correlationId",
            default,
            typeof(DummyMessage),
            "senderId",
            serializer,
            out var envelope);

        // Assert
        Assert.False(result);
        Assert.Null(envelope);
    }

    [Fact]
    public void TryCreate_ShouldReturnFalse_WhenMessageTypeIsNull()
    {
        // Arrange
        var serializer = Substitute.For<ISerializer>();

        // Act
        var result = MessageEnvelope.TryCreate(
            [1, 2, 3],
            "messageId",
            "correlationId",
            DateTimeOffset.UtcNow,
            null!,
            "senderId",
            serializer,
            out var envelope);

        // Assert
        Assert.False(result);
        Assert.Null(envelope);
    }

    [Fact]
    public void TryCreate_ShouldReturnFalse_WhenDeserializedMessageIsNull()
    {
        // Arrange
        var serializer = new MockSerializer();

        ReadOnlySpan<byte> body = [1, 2, 3];

        // Act
        var result = MessageEnvelope.TryCreate(
            body,
            "messageId",
            "correlationId",
            DateTimeOffset.UtcNow,
            typeof(DummyMessage),
            "senderId",
            serializer,
            out var envelope);

        // Assert
        Assert.False(result);
        Assert.Null(envelope);
    }

    [Fact]
    public void TryCreate_ShouldReturnFalse_WhenDeserializedObjectIsNotIMessage()
    {
        // Arrange
        var serializer = new MockSerializer("not an IMessage");

        // Act
        var result = MessageEnvelope.TryCreate(
            [1, 2, 3],
            "messageId",
            "correlationId",
            DateTimeOffset.UtcNow,
            typeof(DummyMessage),
            "senderId",
            serializer,
            out var envelope);

        // Assert
        Assert.False(result);
        Assert.Null(envelope);
    }

    [Fact]
    public void Create_WithSystemInfo_ShouldCreateEnvelopeWithCorrectProperties()
    {
        // Arrange
        var message = new DummyMessage();

        var systemInfo = Substitute.For<ISystemInfo>();
        var systemId = "system-id";
        systemInfo.Id.Returns(systemId);

        // Act
        var now = DateTimeOffset.UtcNow;
        var envelope = MessageEnvelope.Create(message, systemInfo);

        // Assert
        Assert.Equal(message, envelope.Message);
        Assert.Equal(systemId, envelope.SenderId);
        Assert.NotNull(envelope.CorrelationId);
        Assert.NotNull(envelope.MessageId);
        Assert.True(envelope.CreatedAt >= now);
    }

    [Fact]
    public void Create_WithSystemInfo_ShouldThrow_WhenMessageIsNull()
    {
        // Arrange
        var systemInfo = Substitute.For<ISystemInfo>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MessageEnvelope.Create(null!, systemInfo));
    }

    [Fact]
    public void Create_WithSagaInstance_ShouldCreateEnvelopeWithCorrectProperties()
    {
        // Arrange
        var message = new DummyMessage();
        var sagaInstance = Substitute.For<ISagaInstance>();
        var correlationId = Guid.NewGuid().ToString();
        var instanceId = Guid.NewGuid().ToString();
        
        sagaInstance.CorrelationId.Returns(correlationId);
        sagaInstance.InstanceId.Returns(instanceId);

        // Act
        var now = DateTimeOffset.UtcNow;
        var envelope = MessageEnvelope.Create(message, sagaInstance);

        // Assert
        Assert.Equal(message, envelope.Message);
        Assert.Equal(instanceId, envelope.SenderId);
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.NotEmpty(envelope.MessageId);
        Assert.True(envelope.CreatedAt >= now);
    }

    [Fact]
    public void Create_WithSagaInstance_ShouldSetProperMessageIdWhenMessageIdempotent()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var instanceId = Guid.NewGuid().ToString();

        var sagaInstance = Substitute.For<ISagaInstance>();
        sagaInstance.CorrelationId.Returns(correlationId);
        sagaInstance.InstanceId.Returns(instanceId);

        var requestId = Guid.NewGuid().ToString();
        var message = new FakeIdempotentMessage(requestId, 42);

        // Act
        var envelope = MessageEnvelope.Create(message, sagaInstance);

        // Assert
        Assert.Equal(message, envelope.Message);
        Assert.Equal(instanceId, envelope.SenderId);
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.NotEmpty(envelope.MessageId);
    }

    [Fact]
    public void Create_WithSagaInstance_ShouldThrow_WhenMessageIsNull()
    {
        // Arrange
        var sagaInstance = Substitute.For<ISagaInstance>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MessageEnvelope.Create(null!, sagaInstance));
    }

    [Fact]
    public void Create_WithSagaInstance_ShouldThrow_WhenSagaInstanceIsNull()
    {
        // Arrange
        var message = new DummyMessage();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MessageEnvelope.Create(message, (ISagaInstance)null!));
    }

    [Fact]
    public void Create_ShouldUseMessageCorrelationId_WhenMessageImplementsIHasCorrelationId()
    {
        // Arrange
        var messageCorrelationId = Guid.NewGuid().ToString();
        var message = Substitute.For<IMessage, IHasCorrelationId>();
        ((IHasCorrelationId)message).CorrelationId.Returns(messageCorrelationId);
        
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.Id.Returns("system-id");

        // Act
        var envelope = MessageEnvelope.Create(message, systemInfo);

        // Assert
        Assert.Equal(messageCorrelationId, envelope.CorrelationId);
    }

    [Fact]
    public void Create_ShouldUseSagaCorrelationId_WhenMessageDoesNotImplementIHasCorrelationId()
    {
        // Arrange
        var sagaCorrelationId = Guid.NewGuid().ToString();
        var message = new DummyMessage();
        var sagaInstance = Substitute.For<ISagaInstance>();
        sagaInstance.CorrelationId.Returns(sagaCorrelationId);
        sagaInstance.InstanceId.Returns(Guid.NewGuid().ToString());

        // Act
        var envelope = MessageEnvelope.Create(message, sagaInstance);

        // Assert
        Assert.Equal(sagaCorrelationId, envelope.CorrelationId);
    }

    #region Helper Classes

    private class MessageWithCorrelationId : IMessage, IHasCorrelationId
    {
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    }

    #endregion
}
