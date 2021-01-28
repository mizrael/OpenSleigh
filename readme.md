# OpenSleigh
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Core?style=plastic)
[![OpenSleigh](https://circleci.com/gh/mizrael/OpenSleigh.svg?style=shield&circle-token=b7635df8feb7c79524db993c3cf962863ad28aa1)](https://app.circleci.com/pipelines/github/mizrael/OpenSleigh)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=mizrael_OpenSleigh&metric=coverage)](https://sonarcloud.io/dashboard?id=mizrael_OpenSleigh)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=mizrael_OpenSleigh&metric=security_rating)](https://sonarcloud.io/dashboard?id=mizrael_OpenSleigh)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=mizrael_OpenSleigh&metric=reliability_rating)](https://sonarcloud.io/dashboard?id=mizrael_OpenSleigh)

## Description
OpenSleigh is a distributed saga management library, written in C# with .NET Core 5. 
It is intended to be reliable, fast, easy to use, configurable and extensible.

## Installation
OpenSleigh can be installed from Nuget. The Core module is available here: https://www.nuget.org/packages/OpenSleigh.Core/

However, a Transport and Persistence library are necessary to properly use the library.

These are the libraries available at the moment:
- https://www.nuget.org/packages/OpenSleigh.Persistence.InMemory/
- https://www.nuget.org/packages/OpenSleigh.Persistence.Mongo/
- https://www.nuget.org/packages/OpenSleigh.Persistence.SQL/
- https://www.nuget.org/packages/OpenSleigh.Transport.RabbitMQ/

## How-to
OpenSleigh is intended to be flexible and developer friendly. It makes use of Dependency Injection for its own initialization and the setup of the dependencies.

The first step, once you have installed the [Core library](https://www.nuget.org/packages/OpenSleigh.Core/), is to add OpenSleigh to the Services collection:

```
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
                services.AddOpenSleigh(cfg =>{ ... });
    });
```

#### Configuring Transport and Persistence
OpenSleigh needs to be configured to point to a specific Transport bus and a Persistence mechanism for the Saga States:
```
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
                services.AddOpenSleigh(cfg =>{ 
            var rabbitSection = hostContext.Configuration.GetSection("Rabbit");
            var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"], 
                rabbitSection["UserName"],
                rabbitSection["Password"]);

            gfg.UseRabbitMQTransport(rabbitCfg);

            var mongoSection = hostContext.Configuration.GetSection("Mongo");
            var mongoCfg = new MongoConfiguration(mongoSection["ConnectionString"],
                mongoSection["DbName"],
                MongoSagaStateRepositoryOptions.Default);

            cfg.UseMongoPersistence(mongoCfg);
        });
    });
```
In this example, the system is configured to use RabbitMQ as message bus and MongoDB to persist the data.

##### For detailed instructions on each Transport and Persistence library, please refer to the specific README file located in the library's root folder.

#### Adding a Saga

A Saga is a simple class inheriting from the base [`Saga<>`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh.Core/Saga.cs) class. We also have to create an additional State class holding it's data, by inheriting from [`SagaState`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh.Core/SagaState.cs):

```
public class MyAwesomeSagaState : SagaState{
    public MyAwesomeSagaState(Guid id) : base(id){}
}

public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>
{
    private readonly ILogger<MyAwesomeSaga> _logger;       

    public ParentSaga(ILogger<MyAwesomeSaga> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

Dependency injection can be used to reference services from Sagas.

**IMPORTANT**: 
Each Saga should have its own State class. Don't reuse State classes!

At this point all you have to do is register and configure the Saga:
```
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<MyAwesomeSaga, MyAwesomeSagaState>()      
        .UseRabbitMQTransport(rabbitConfig);
});
```

When adding a Saga, it's important to specify the Transport we want to use for its messages (inbound and outbound). In this example we're using the `UseRabbitMQTransport()` extension method, which tells OpenSleight to use RabbitMQ for this Saga.

#### Starting a Saga
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

#### Handling messages

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

#### Stop Messages execution

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

#### Publishing messages
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

#### Samples
Samples are available in the `/samples/` folder.

- **[Sample1](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample1)** is a simple .NET Console Application showing how to bootstrap OpenSleigh and use In-Memory persistence and transport.

- **[Sample2](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample2)** is a more interesting scenario, with a Web API acting as message producer and a Console Application as subscriber. This example uses RabbitMQ and MongoDB. It also uses a custom naming policy for some messages, which allows greater flexibility with exchanges and queue generation.


## Roadmap
- add more tests
- add more logging
- add Azure ServiceBus message transport
- add CosmosDB saga state persistence
