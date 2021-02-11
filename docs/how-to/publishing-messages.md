---
layout: default
---

# Publishing messages
A message can be published by calling the `PublishAsync()` method of `IMessageBus`. Sagas classes get an instance injected as Property:

```
public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>,
    IStartedBy<StartMyAwesomeSaga>
{
    public async Task HandleAsync(IMessageContext<StartMyAwesomeSaga> context, CancellationToken cancellationToken = default)
    {
        var message = new MyAwesomeSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
        this.Bus.PublishAsync(message);
    }
}
```
OpenSleigh uses the [Outbox pattern](https://www.davideguida.com/improving-microservices-reliability-part-2-outbox-pattern/) to ensure messages are properly published and the Saga State is persisted.

## Publish-only applications
An application can be also configured as *"publish-only"*: it will only take care of dispatching new messages but won't be able to consume any. Useful when creating a Web API which offloads the actual execution to a separate worker service.
