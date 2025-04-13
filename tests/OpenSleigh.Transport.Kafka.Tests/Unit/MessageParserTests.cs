using Confluent.Kafka;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Outbox;
using System;
using System.Text;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class MessageParserTests
{
    [Fact]
    public void Resolve_should_throw_when_input_null()
    {          
        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        var sut = new MessageParser(queueReferenceFactory);

        Assert.Throws<ArgumentNullException>(() => sut.Parse(null));
    }

    [Fact]
    public void Resolve_should_throw_when_headers_do_not_contain_message_type()
    {
        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        var sut = new MessageParser(queueReferenceFactory);

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
        var sut = new MessageParser(queueReferenceFactory);

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
;       var message = DummyMessage.CreateOutboxMessage(parentId);
        var messageType = typeof(DummyMessage);
        var encodedMessage = Newtonsoft.Json.JsonConvert.SerializeObject(message);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType); 
        
        var sut = new MessageParser(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Value = message.Body.ToArray(),
                Headers = [
                    new Header(nameof(OutboxMessage.MessageId), Encoding.UTF8.GetBytes(message.MessageId)),
                    new Header(nameof(OutboxMessage.SenderId), Encoding.UTF8.GetBytes(message.SenderId)),
                    new Header(nameof(OutboxMessage.CorrelationId), Encoding.UTF8.GetBytes(message.CorrelationId)),
                    new Header(nameof(OutboxMessage.CreatedAt), Encoding.UTF8.GetBytes(message.CreatedAt.ToString())),
                    new Header(nameof(OutboxMessage.ParentId), Encoding.UTF8.GetBytes(message.ParentId)),
                ]
            }
        };
        var result = sut.Parse(consumeResult);
        Assert.NotNull(result);
        Assert.Equal(message.MessageId, result.MessageId);
        Assert.Equal(message.SenderId, result.SenderId);
        Assert.Equal(message.CorrelationId, result.CorrelationId);
        Assert.Equal(message.CreatedAt, result.CreatedAt, TimeSpan.FromSeconds(2));
        Assert.Equal(message.ParentId, result.ParentId);
        Assert.Equal(message.Body.ToArray(), result.Body.ToArray());
        Assert.Equal(message.MessageType, result.MessageType);
    }

    [Fact]
    public void Resolve_should_throw_when_MessageId_header_missing()
    {
        var messageTopic = "DummyMessage";     
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = new MessageParser(queueReferenceFactory);

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
        Assert.Contains(nameof(OutboxMessage.MessageId), ex.Message);
    }

    [Fact]
    public void Resolve_should_throw_when_SenderId_header_missing()
    {
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = new MessageParser(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Headers = [
                    new Header(nameof(OutboxMessage.MessageId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                ],
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains(nameof(OutboxMessage.SenderId), ex.Message);
    }

    [Fact]
    public void Resolve_should_throw_when_CorrelationId_header_missing()
    {
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = new MessageParser(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Headers = [
                    new Header(nameof(OutboxMessage.MessageId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(OutboxMessage.SenderId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                ],
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains(nameof(OutboxMessage.CorrelationId), ex.Message);
    }

    [Fact]
    public void Resolve_should_throw_when_CreatedAt_header_missing()
    {
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = new MessageParser(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Headers = [
                    new Header(nameof(OutboxMessage.MessageId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(OutboxMessage.SenderId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(OutboxMessage.CorrelationId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                ],
                Value = Array.Empty<byte>()
            }
        };
        var ex = Assert.Throws<ArgumentException>(() => sut.Parse(consumeResult));
        Assert.Contains(nameof(OutboxMessage.CreatedAt), ex.Message);
    }

    [Fact]
    public void Resolve_should_not_throw_when_ParentId_header_missing()
    {
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = new MessageParser(queueReferenceFactory);

        var consumeResult = new ConsumeResult<string, byte[]>()
        {
            Topic = messageTopic,
            Message = new Message<string, byte[]>()
            {
                Headers = [
                    new Header(nameof(OutboxMessage.MessageId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(OutboxMessage.SenderId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(OutboxMessage.CorrelationId), Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(nameof(OutboxMessage.CreatedAt), Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("o"))),
                ],
                Value = new byte[] {1,2,3}
            }
        };
        sut.Parse(consumeResult);
    }

    [Fact]
    public void Resolve_should_throw_when_headers_missing()
    {
        var messageTopic = "DummyMessage";
        var messageType = typeof(DummyMessage);

        var queueReferenceFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.GetQueueType(messageTopic).Returns(messageType);

        var sut = new MessageParser(queueReferenceFactory);

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
