using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Transport;

public class DefaultMessageContextTests
{
    [Fact]
    public void Create_ShouldReturnValidMessageContext_WhenOutboxMessageIsValid()
    {
        // Arrange
        var message = new DummyMessage();
        var outboxMessage = DummyMessage.CreateEnvelope();

        // Act
        var result = DefaultMessageContext<DummyMessage>.Create(outboxMessage);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(outboxMessage.MessageId, result.MessageId);
        Assert.Equal(outboxMessage.CorrelationId, result.CorrelationId);
        Assert.Equal(outboxMessage.SenderId, result.SenderId);
        Assert.IsType<DummyMessage>(result.Message);
    }

    [Fact]
    public void Create_ShouldReturnCorrectImplementation_WhenOutboxMessageIsValid()
    {
        // Arrange
        var message = new DummyMessage();
        var outboxMessage = DummyMessage.CreateEnvelope();

        // Act
        var result = DefaultMessageContext<DummyMessage>.Create(outboxMessage);

        // Assert
        Assert.IsAssignableFrom<IMessageContext<DummyMessage>>(result);
    }

    [Fact]
    public void Create_should_throw_when_input_null()
    {
        Assert.Throws<ArgumentNullException>(() => DefaultMessageContext<DummyMessage>.Create(null!));
    }
}
