using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Transport;

public class IdempotentMessageExtensionsTests
{
    [Fact]
    public void GetIdempotencyKey_should_return_hash_based_on_components()
    {
        var message = new FakeIdempotentMessage("req-1", 42);

        var key = message.GetIdempotencyKey();

        Assert.NotNull(key);
        Assert.Equal(64, key.Length);
    }

    [Fact]
    public void GetIdempotencyKey_should_return_same_key_for_same_input()
    {
        var message1 = new FakeIdempotentMessage("req-1", 42);
        var message2 = new FakeIdempotentMessage("req-1", 42);

        Assert.Equal(message1.GetIdempotencyKey(), message2.GetIdempotencyKey());
    }

    [Fact]
    public void GetIdempotencyKey_should_return_different_key_for_different_input()
    {
        var message1 = new FakeIdempotentMessage("req-1", 42);
        var message2 = new FakeIdempotentMessage("req-2", 42);

        Assert.NotEqual(message1.GetIdempotencyKey(), message2.GetIdempotencyKey());
    }

    [Fact]
    public void GetIdempotencyKey_should_include_correlationId_when_present()
    {
        var message1 = new FakeIdempotentMessageWithCorrelation("req-1", 42, "corr-1");
        var message2 = new FakeIdempotentMessageWithCorrelation("req-1", 42, "corr-2");

        Assert.NotEqual(message1.GetIdempotencyKey(), message2.GetIdempotencyKey());
    }
}

internal record FakeIdempotentMessageWithCorrelation(string RequestId, int Foo, string CorrelationId) 
    : IIdempotentMessage, IHasCorrelationId
{
    public IEnumerable<object> GetIdempotencyComponents()
    {
        yield return Foo;
    }
}
