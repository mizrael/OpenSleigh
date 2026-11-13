---
layout: default
title: Handling Messages
parent: How-To
nav_order: 3
---

# Handling Messages

## Starting a Saga

In order to start a Saga, we need to tell OpenSleigh which message type can be used as "initiator". To do that, we need to add the [`IStartedBy<>`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Transport/IStartedBy.cs) interface to the Saga and implement it:

```csharp
public class MyAwesomeSaga :
    Saga,
    IStartedBy<StartMyAwesomeSaga>
{
    public async ValueTask HandleAsync(
        IMessageContext<StartMyAwesomeSaga> context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"starting saga '{context.Message.CorrelationId}'...");
    }
}
```

Messages are simple POCO classes (or records), implementing the [`IMessage`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Transport/IMessage.cs) interface:

```csharp
public record StartMyAwesomeSaga() : IMessage { }
```

## Handling messages

In order to handle more message types, it is necessary to add and implement the [`IHandleMessage<>`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Transport/IHandleMessage.cs) interface on the Saga:

```csharp
public class MyAwesomeSaga :
    Saga,
    IStartedBy<StartMyAwesomeSaga>,
    IHandleMessage<MyAwesomeSagaCompleted>
{
    // code omitted for brevity

    public async ValueTask HandleAsync(
        IMessageContext<MyAwesomeSagaCompleted> context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"saga '{context.Message.CorrelationId}' completed!");
    }
}
```

## Stopping a Saga

A Saga can be marked as completed by calling the `MarkAsCompleted()` on its state:

```csharp
public class MyAwesomeSaga :
    Saga,
    IStartedBy<StartMyAwesomeSaga>,
    IHandleMessage<MyAwesomeSagaCompleted>
{
    // code omitted for brevity

    public async ValueTask HandleAsync(
        IMessageContext<MyAwesomeSagaCompleted> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.MarkAsCompleted();
    }
}
```

This step is completely optional but signals OpenSleigh to stop sending messages to that Saga instance.

## When a handler fails

When `HandleAsync` throws, OpenSleigh calls `RollbackAsync` for the same message on the same Saga. It does nothing by default; implement it to undo what the handler did, or to publish compensating messages:

```csharp
public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>,
    IStartedBy<StartMyAwesomeSaga>
{
    // code omitted for brevity

    public async ValueTask HandleAsync(
        IMessageContext<StartMyAwesomeSaga> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.State.Reserved = true;
        this.Publish(new ShipOrder());
        throw new InvalidOperationException("out of stock");
    }

    public ValueTask RollbackAsync(
        IMessageContext<StartMyAwesomeSaga> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.State.Reserved = false;
        this.Publish(new CancelOrder());
        return ValueTask.CompletedTask;
    }
}
```

What happens then:

- The messages the handler published before it failed are discarded (`ShipOrder` above). The messages published by `RollbackAsync` are stored together with the state of the Saga and delivered, exactly like the messages of a handler that succeeded.
- The state of the Saga is stored as the handler and the rollback left it. Changes made by the failed handler are not undone for you: `RollbackAsync` is where to undo them.
- The message is recorded as processed by that Saga, and is sent to the dead letter destination of the transport once every Saga registered for it has handled it. It is not processed again: if it is delivered again, or replayed from the dead letter destination, a Saga that rolled it back ignores it, so the rollback never runs twice. To try again, publish a new message.
- If `RollbackAsync` throws as well, nothing it published is stored, the Saga is released as it is, and the message is sent to the dead letter destination without being recorded as processed.
- A Saga whose handler fails does not prevent the other Sagas registered for the same message from handling it.

A handler failure is never retried. Only a lock conflict is, that is a message for a Saga instance that another worker is processing at that moment: the message is delivered again after a delay, for as long as the conflict lasts.

A handler, or a rollback, that is interrupted because the application is stopping (it observes the cancellation token and throws `OperationCanceledException`) has not failed: nothing it published is stored, the message is not recorded as processed, and it is delivered again. The same goes for a message whose state and messages could not be stored once its handler had run.
