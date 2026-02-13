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
