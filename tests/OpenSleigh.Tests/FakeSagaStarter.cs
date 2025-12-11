using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class FakeSagaStarter : IMessage { }

public class OtherFakeSagaStarter : IMessage { }

public class FakeSagaMessage : IMessage { }

public record FakeIdempotentMessage(string RequestId, int Foo) : IIdempotentMessage
{
    public IEnumerable<object> GetIdempotencyComponents()
    {
        yield return Foo;
    }
}
