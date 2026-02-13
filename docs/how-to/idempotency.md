---
layout: default
title: Idempotency
parent: How-To
nav_order: 7
---

# Idempotency

By design, OpenSleigh uses the [Outbox pattern](https://www.davidguida.net/improving-microservices-reliability-part-2-outbox-pattern/) to ensure at-least-once delivery. This guarantees the reliability of the publisher side, but we still need to rely on the transport mechanism to deliver the message.

On the receiver side, we can expect messages to be processed as they arrive. But what happens when the same message is published more than once?

OpenSleigh assigns an ID to each outgoing message, which is then used on the receiver side to ensure at-most-once processing. When a message is received, OpenSleigh fetches the metadata of the desired Saga, locks it so no other service instance can access it, and compares the message ID against the list of all the messages processed so far.

Normally, this message ID is a simple [GUID v7](https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7?view=net-9.0), but it is also possible to customize the ID generation by having your message class implement [`IIdempotentMessage`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Transport/IIdempotentMessage.cs) instead of [`IMessage`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Transport/IMessage.cs):

```csharp
public record IdempotentMessage(string RequestId, int Foo) : IIdempotentMessage
{
    public IEnumerable<object> GetIdempotencyComponents()
    {
        yield return this.Foo;
    }
}
```

The `GetIdempotencyComponents` property helps OpenSleigh pick which elements of the message have to be utilized to build the _idempotency key_ that will later on be used as message ID.
