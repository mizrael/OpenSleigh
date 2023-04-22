---
layout: default
title: Stateless Message Handlers
description: how to handle messages with a simple handler with no Saga State.
---

# [How-to](/how-to/) / Stateless Message Handlers
Sagas are not the only way to process messages. Sometimes the requirement might be simple enough, or they just don't require any state persistence. In these situations, OpenSleigh can use *Stateless handlers*.

All you have to do is create a simple class and implement the `IHandleMessage<>` interface:

```csharp
public class MySimpleHandler : IHandleMessage<MyMessage>
{  
    public async Task HandleAsync(IMessageContext<MyMessage> context, CancellationToken cancellationToken = default)
    {
        // do some async stuff here...
    }
}
```
These handlers are registered with the Transient lifetime on your DI container, can have dependencies injected, and don't hold any State.

For example, they can be used to handle a user notification process or a fire-and-forget operation.

For more details, check <a href='https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample11' target='_blank'>Sample 11</a> for an example with Kafka and <a href='https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample12' target='_blank'>Sample 12</a> for one with Azure ServiceBus.