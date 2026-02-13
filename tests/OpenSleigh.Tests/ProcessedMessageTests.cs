using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class ProcessedMessageTests
{
    [Fact]
    public void Create_should_set_MessageId_from_context()
    {
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(
            new FakeSagaStarter(), messageId: "msg-123");

        var result = ProcessedMessage.Create(messageContext);

        Assert.Equal("msg-123", result.MessageId);
        Assert.True(result.When <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GetHashCode_should_be_based_on_MessageId()
    {
        var pm1 = new ProcessedMessage { MessageId = "same-id", When = DateTimeOffset.UtcNow };
        var pm2 = new ProcessedMessage { MessageId = "same-id", When = DateTimeOffset.UtcNow.AddHours(1) };

        Assert.Equal(pm1.GetHashCode(), pm2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_should_differ_for_different_MessageIds()
    {
        var pm1 = new ProcessedMessage { MessageId = "id-1", When = DateTimeOffset.UtcNow };
        var pm2 = new ProcessedMessage { MessageId = "id-2", When = DateTimeOffset.UtcNow };

        Assert.NotEqual(pm1.GetHashCode(), pm2.GetHashCode());
    }
}
