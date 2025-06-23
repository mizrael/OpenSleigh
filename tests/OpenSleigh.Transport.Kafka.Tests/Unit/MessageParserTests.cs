using Confluent.Kafka;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Outbox;
using System;
using System.Text;
using System.Text.Json;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class MessageParserTests
{
    private static MessageParser CreateSUT(IQueueReferenceFactory? queueReferenceFactory = null)
    {
        queueReferenceFactory ??= NSubstitute.Substitute.For<IQueueReferenceFactory>();
        var sut = new MessageParser(queueReferenceFactory, new Utils.JsonSerializer());
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
        ex.Message.Should().Contain("invalid message type");
    }

    [Fact]
    public void Resolve_should_throw_when_message_type_header_does_not_match()
    {
        Type messageType = null;
        var messageTopic = "lorem";

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);
        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic= messageTopic,
            Message = new Message<string, byte[]>()
        };
        
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        ex.Message.Should().Contain("invalid message type");
    }

    [Fact]
    public void Resolve_should_return_message()
    {
        var messageTopic = nameof(DummyMessage);
        var parentId = "parent id";
;       var envelope = DummyMessage.CreateEnvelope(parentId);
        var jsonMessage = Newtonsoft.Json.JsonConvert.SerializeObject(envelope.Message);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(envelope.MessageType);

        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Key = envelope.MessageId,
                Value = Encoding.UTF8.GetBytes(jsonMessage),
                Headers = [
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
        var messageTopic = "DummyMessage";     
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Headers = new(),
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains("message id cannot be null", ex.Message);
    }

    [Fact]
    public void Resolve_should_throw_when_SenderId_header_missing()
    {
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
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
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
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
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
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
        var messageTopic = "DummyMessage";
        var envelope = DummyMessage.CreateEnvelope();
        var jsonMessage = Newtonsoft.Json.JsonConvert.SerializeObject(envelope.Message);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(envelope.MessageType);

        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Key = Guid.NewGuid().ToString(),
                Headers = [
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
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = CreateSUT(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains("message headers cannot be null.", ex.Message);
    }
}
