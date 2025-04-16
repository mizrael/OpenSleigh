using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class FakeSagaStarter : IMessage { }

public class FakeSagaMessage : IMessage { }

public record FakeIdempotentMessage(string IdempotencyKey) : IMessage, IHasIdempotencyKey;