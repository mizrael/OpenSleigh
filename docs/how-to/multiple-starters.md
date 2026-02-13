---
layout: default
title: Multiple Starters
parent: How-To
nav_order: 4
---

# Multiple Saga Starters

By default, a saga is initiated by a single message type via the `IStartedBy<TMessage>` interface. Starting with v3.1.0, a saga can implement **multiple** `IStartedBy<>` interfaces, allowing it to be started by different message types.

## Use Cases

This is useful when a workflow can be triggered from different entry points. For example, an order processing saga might start when either an order is placed by a customer or when a payment is received from a payment gateway:

```csharp
public record OrderPlaced(string OrderId) : IMessage;
public record PaymentReceived(string OrderId, decimal Amount) : IMessage;
public record ShipOrder : IMessage;

public class OrderSaga :
    Saga<OrderState>,
    IStartedBy<OrderPlaced>,
    IStartedBy<PaymentReceived>,
    IHandleMessage<ShipOrder>
{
    public OrderSaga(ISagaInstance<OrderState> context) : base(context) { }

    public ValueTask HandleAsync(
        IMessageContext<OrderPlaced> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.State.OrderId = context.Message.OrderId;
        this.Context.State.IsOrderReceived = true;

        if (this.Context.State.IsPaymentReceived)
            this.Publish(new ShipOrder());

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(
        IMessageContext<PaymentReceived> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.State.OrderId = context.Message.OrderId;
        this.Context.State.IsPaymentReceived = true;

        if (this.Context.State.IsOrderReceived)
            this.Publish(new ShipOrder());

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(
        IMessageContext<ShipOrder> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}
```

## How It Works

When a starter message arrives, OpenSleigh looks up the saga instance by its correlation ID:

- If **no instance exists**, a new one is created.
- If an instance **already exists** (created by a different starter message), the message is handled as a regular message on the existing instance.

## Concurrency and Optimistic Locking

When multiple starter messages for the same correlation ID arrive at the same time, OpenSleigh uses optimistic concurrency control to ensure only **one** saga instance is created. If a concurrent creation attempt is detected, an `OptimisticLockException` is thrown internally and the operation is retried — the second message is then handled on the existing instance rather than creating a duplicate.

This is handled automatically by the framework; no additional code is needed in your saga implementation.
