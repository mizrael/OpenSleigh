using Confluent.Kafka;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Text;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class MessageParserTests
{
    private static KafkaMessageParser CreateSUT(ITypeResolver? typeResolver = null)
    {
        typeResolver ??= NSubstitute.Substitute.For<ITypeResolver>();
        var sut = new KafkaMessageParser(typeResolver, new Utils.JsonSerializer());
        return sut;
    }

    [Fact]
    public void Resolve_should_throw_when_input_null()
    {
        var sut = CreateSUT();

        Assert.Throws<ArgumentNullException>(() => sut.Parse(null));
    }

    [Fact]
    public void Resolve_should_throw_when_headers_do_not_contain_message_type()
    {
        var sut = CreateSUT();

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Message = new Message<string, byte[]>()
            {
                Headers = new Headers()
            }
        };

        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
    }

    [Fact]
    public void Resolve_should_throw_when_message_type_header_does_not_match()
    {
        Type messageType = null;
        var messageTypeName = "lorem";

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(messageType);
        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic= "some-topic",
            Message = new Message<string, byte[]>()
            {
                Headers = new Headers
                {
                    { nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName) }
                }
            }
        };

        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains("invalid message type", ex.Message);
    }

    [Fact]
    public void Resolve_should_return_message()
    {
        var messageTopic = nameof(DummyMessage);
        var messageTypeName = typeof(DummyMessage).AssemblyQualifiedName;
        var parentId = "parent id";
        var envelope = DummyMessage.CreateEnvelope(parentId);
        var jsonMessage = Newtonsoft.Json.JsonConvert.SerializeObject(envelope.Message);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(envelope.MessageType);

        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Key = envelope.MessageId,
                Value = Encoding.UTF8.GetBytes(jsonMessage),
                Headers = [
                    new Header(nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName)),
                    new Header(nameof(MessageEnvelope.MessageId), Encoding.UTF8.GetBytes(envelope.MessageId)),
                    new Header(nameof(MessageEnvelope.SenderId), Encoding.UTF8.GetBytes(envelope.SenderId)),
                    new Header(nameof(MessageEnvelope.CorrelationId), Encoding.UTF8.GetBytes(envelope.CorrelationId)),
                    new Header(nameof(MessageEnvelope.CreatedAt), Encoding.UTF8.GetBytes(envelope.CreatedAt.ToString())),
                ]
            }
        };
        var result = sut.Parse(consumeResult);
        Assert.NotNull(result);
        Assert.Equal(envelope.MessageId, result.MessageId);
        Assert.Equal(envelope.SenderId, result.SenderId);
        Assert.Equal(envelope.CorrelationId, result.CorrelationId);
        Assert.Equal(envelope.CreatedAt, result.CreatedAt, TimeSpan.FromSeconds(2));
        Assert.Equal(envelope.Message, result.Message);
        Assert.Equal(envelope.MessageType, result.MessageType);
    }

    [Fact]
    public void Resolve_should_throw_when_MessageId_missing()
    {
        var messageTypeName = typeof(DummyMessage).AssemblyQualifiedName;
        var messageType = typeof(DummyMessage);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(messageType);

        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = "some-topic",
            Message = new Message<string, byte[]>()
            {
                Headers = new Headers
                {
                    { nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName) }
                },
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains("message id cannot be null", ex.Message);
    }

    [Fact]
    public void Resolve_should_throw_when_SenderId_header_missing()
    {
        var messageTypeName = typeof(DummyMessage).AssemblyQualifiedName;
        var messageType = typeof(DummyMessage);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(messageType);

        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = "some-topic",
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
                    new Header(nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName))
                ],
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains(nameof(MessageEnvelope.SenderId), ex.Message);
    }

    [Fact]
    public void Resolve_should_throw_when_CorrelationId_header_missing()
    {
        var messageTypeName = typeof(DummyMessage).AssemblyQualifiedName;
        var messageType = typeof(DummyMessage);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(messageType);

        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = "some-topic",
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
                    new Header(nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName)),
                    new Header(nameof(MessageEnvelope.MessageId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(MessageEnvelope.SenderId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                ],
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains(nameof(MessageEnvelope.CorrelationId), ex.Message);
    }

    [Fact]
    public void Resolve_should_throw_when_CreatedAt_header_missing()
    {
        var messageTypeName = typeof(DummyMessage).AssemblyQualifiedName;
        var messageType = typeof(DummyMessage);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(messageType);

        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = "some-topic",
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
                    new Header(nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName)),
                    new Header(nameof(MessageEnvelope.MessageId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(MessageEnvelope.SenderId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(MessageEnvelope.CorrelationId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                ],
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains(nameof(MessageEnvelope.CreatedAt), ex.Message);
    }

    [Fact]
    public void Resolve_should_not_throw_when_ParentId_header_missing()
    {
        var messageTypeName = typeof(DummyMessage).AssemblyQualifiedName;
        var envelope = DummyMessage.CreateEnvelope();
        var jsonMessage = Newtonsoft.Json.JsonConvert.SerializeObject(envelope.Message);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(envelope.MessageType);

        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = "some-topic",
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
                    new Header(nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName)),
                    new Header(nameof(MessageEnvelope.SenderId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(MessageEnvelope.CorrelationId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(MessageEnvelope.CreatedAt), Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("o"))),
                ],
                Value = Encoding.UTF8.GetBytes(jsonMessage)
            }
        };
        var message = sut.Parse(consumeResult);
        Assert.NotNull(message);
    }

    [Fact]
    public void Resolve_should_throw_when_headers_missing()
    {
        var messageType = typeof(DummyMessage);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        var sut = CreateSUT(typeResolver);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = "some-topic",
            Message = new Message<string, byte[]>()
            {
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains("message headers cannot be null.", ex.Message);
    }

    [Fact]
    public void Parse_should_use_MessageType_header_not_topic_name()
    {
        // Arrange
        var messageTypeName = typeof(DummyMessage).AssemblyQualifiedName;
        var envelope = DummyMessage.CreateEnvelope();
        var jsonMessage = Newtonsoft.Json.JsonConvert.SerializeObject(envelope.Message);

        var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
        typeResolver.Resolve(messageTypeName).Returns(typeof(DummyMessage));

        var sut = CreateSUT(typeResolver);

        // Topic name is intentionally different from the message type
        var differentTopicName = "wrong-topic-name";
        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = differentTopicName,
            Message = new Message<string, byte[]>()
            {
                Key = envelope.MessageId,
                Value = Encoding.UTF8.GetBytes(jsonMessage),
                Headers = [
                    new Header(nameof(MessageEnvelope.MessageType), Encoding.UTF8.GetBytes(messageTypeName)),
                    new Header(nameof(MessageEnvelope.SenderId), Encoding.UTF8.GetBytes(envelope.SenderId)),
                    new Header(nameof(MessageEnvelope.CorrelationId), Encoding.UTF8.GetBytes(envelope.CorrelationId)),
                    new Header(nameof(MessageEnvelope.CreatedAt), Encoding.UTF8.GetBytes(envelope.CreatedAt.ToString())),
                ]
            }
        };

        // Act
        var result = sut.Parse(consumeResult);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(typeof(DummyMessage), result.MessageType);

        // Verify that the type resolver was called with the header value, NOT the topic name
        typeResolver.Received(1).Resolve(messageTypeName);
        typeResolver.DidNotReceive().Resolve(differentTopicName);
    }
}
