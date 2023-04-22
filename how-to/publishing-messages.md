---
layout: default
title: Publishing messages
description: publishing message on the configured Message Broker, using the Outbox Pattern
---

# [How-to](/how-to/) / Publishing messages
A message can be published by calling the `Publish()` method directly on the Saga instance:

```
public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>,
    IStartedBy<StartMyAwesomeSaga>
{
    public async Task HandleAsync(IMessageContext<StartMyAwesomeSaga> context, CancellationToken cancellationToken = default)
    {
        var message = new MyAwesomeSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
        this.Publish(message);
    }
}
```

OpenSleigh uses the [Outbox pattern](https://www.davideguida.com/improving-microservices-reliability-part-2-outbox-pattern/) to ensure messages are properly published and the Saga State is persisted.

## Publish-only applications
An application can be also configured as *"publish-only"*: it will only take care of dispatching new messages but won't be able to consume any. Useful when creating a Web API which offloads the actual execution to a separate worker service.
