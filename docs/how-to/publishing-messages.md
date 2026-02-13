---
layout: default
title: Publishing Messages
parent: How-To
nav_order: 5
---

# Publishing Messages

A message can be published by calling the `Publish()` method directly on the Saga instance:

```csharp
public class MyAwesomeSaga :
    Saga,
    IStartedBy<StartMyAwesomeSaga>
{
    public async ValueTask HandleAsync(
        IMessageContext<StartMyAwesomeSaga> context,
        CancellationToken cancellationToken = default)
    {
        var message = new MyAwesomeSagaCompleted(
            Guid.NewGuid(), context.Message.CorrelationId);
        this.Publish(message);
    }
}
```

OpenSleigh uses the [Outbox pattern](https://www.davidguida.net/improving-microservices-reliability-part-2-outbox-pattern/) to ensure messages are properly published and the Saga State is persisted.

## Publish-Only Mode

An application can be also configured as _"publish-only"_: it will only take care of dispatching new messages but won't be able to consume any. Useful when creating a Web API that offloads the actual execution to a separate worker service.

```csharp
services.AddOpenSleigh(cfg =>
{
    // code omitted
    cfg.SetPublishOnly();
});
```
