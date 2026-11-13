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

## Delayed messages and timeouts

`Publish()` also takes a delay. The message is stored with the Saga State like any other, stays in the outbox until the delay has passed, and is then published as usual.

A Saga can use it to stop waiting for a reply: it publishes its own timeout message next to the request, and handles it like any other message.

```csharp
public record PaymentTimedOut(Guid OrderId) : IMessage;

public class OrderSaga :
    Saga<OrderState>,
    IStartedBy<PlaceOrder>,
    IHandleMessage<PaymentCompleted>,
    IHandleMessage<PaymentTimedOut>
{
    public ValueTask HandleAsync(
        IMessageContext<PlaceOrder> context,
        CancellationToken cancellationToken = default)
    {
        this.Publish(new RequestPayment(context.Message.OrderId));
        this.Publish(new PaymentTimedOut(context.Message.OrderId), TimeSpan.FromMinutes(15));
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(
        IMessageContext<PaymentCompleted> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.State.Paid = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(
        IMessageContext<PaymentTimedOut> context,
        CancellationToken cancellationToken = default)
    {
        if (this.Context.State.Paid)
            return ValueTask.CompletedTask;

        this.Publish(new CancelOrder(context.Message.OrderId));
        this.Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}
```

What to expect:

- The message is never published before the delay has passed. It is published at the first read of the outbox after that, so it can be late by up to the outbox interval (`OutboxProcessorOptions.Interval`, 5 seconds by default), and by more when the outbox has a backlog. It is a timeout, not a precise timer.
- It is stored in the same commit as the Saga State: a handler that fails does not leave a timeout behind, and a timeout that was stored survives a restart.
- There is nothing to cancel. A delayed message that becomes due after its Saga was marked as completed is not handled. If the Saga is still running, the handler decides from the state whether the timeout still matters, as in the example.
- The delay is counted on the clock of the application that publishes, and compared with the clock of the application that reads the outbox: keep the clocks of your instances in sync.
- It works the same way with every transport and every persistence provider. Upgrading an application that uses SQL Server, PostgreSQL or MongoDB changes how the outbox is stored, and instances of an earlier version must be gone before the first message is published with a delay: see the README of the provider.

## Publish-Only Mode

An application can be also configured as _"publish-only"_: it will only take care of dispatching new messages but won't be able to consume any. Useful when creating a Web API that offloads the actual execution to a separate worker service.

```csharp
services.AddOpenSleigh(cfg =>
{
    // code omitted
    cfg.SetPublishOnly();
});
```
