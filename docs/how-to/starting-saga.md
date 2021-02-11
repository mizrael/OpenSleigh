---
layout: default
---

# Starting a Saga
In order to start a Saga, we need to tell OpenSleigh which message type can be used as "initiator". In order to do that, we need to add  the [`IStartedBy<>`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh.Core/IStartedBy.cs) interface to the Saga and implement it:

```
public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>,
    IStartedBy<StartMyAwesomeSaga>
{
    public async Task HandleAsync(IMessageContext<StartMyAwesomeSaga> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"starting saga '{context.Message.CorrelationId}'...");
    }
}
```

Messages are simple POCO classes (or records), implementing the [`ICommand`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh.Core/ICommand.cs) interface:

```
public record StartMyAwesomeSaga(Guid Id, Guid CorrelationId) : ICommand { }
```
Each message has to expose an `Id` property and a `CorrelationId`. Those are used to reconstruct the Saga State when the message is received by a subscriber. 

**IMPORTANT**: 
If a Saga is sending a message to itself (loopback), or spawning child Sagas, the `CorrelationId` has to be kept unchanged on all the messages. 
Also, make sure the `Id` and the `CorrelationId` don't match!

We also have to specify the starting message for a Saga when registering it on our DI container, by calling the `UseStateFactory()` method:

```
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<MyAwesomeSaga, MyAwesomeSagaState>()
        .UseStateFactory<StartMyAwesomeSaga>(msg => new MyAwesomeSagaState(msg.CorrelationId))
        .UseRabbitMQTransport(rabbitConfig);
});
```
This call will tell OpenSleigh how it can build the initial State for the current Saga when loading it for the first time.

## Handling messages

In order to handle more message types, it is necessary to add and implement the [`IHandleMessage<>`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh.Core/IHandleMessage.cs) interface:

```
public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>,
    IStartedBy<StartMyAwesomeSaga>,
    IHandleMessage<MyAwesomeSagaCompleted>,
{
    // code omitted for brevity

    public async Task HandleAsync(IMessageContext<MyAwesomeSagaCompleted> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"saga '{context.Message.CorrelationId}' completed!");
    }
}
```

## Stopping a Saga

A Saga can be marked as completed by calling the `MarkAsCompleted()` on its state:

```
public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>,
    IStartedBy<StartMyAwesomeSaga>,
    IHandleMessage<MyAwesomeSagaCompleted>,
{
    // code omitted for brevity

    public async Task HandleAsync(IMessageContext<MyAwesomeSagaCompleted> context, CancellationToken cancellationToken = default)
    {
        this.State.MarkAsCompleted();
    }
}
```
A completed Saga will not handle messages anymore. 