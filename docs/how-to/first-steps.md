---
layout: default
title: First Steps
parent: How-To
nav_order: 2
---

# First Steps

OpenSleigh is intended to be flexible and developer-friendly. It makes use of Dependency Injection for its own initialization and the setup of the dependencies.

## Configure Transport and Persistence

The first step, once you have installed the [Core library](https://www.nuget.org/packages/OpenSleigh/), is to add OpenSleigh to the Services collection:

```csharp
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
                services.AddOpenSleigh(cfg =>{ ... });
    });
```

OpenSleigh needs to be configured to point to a specific Transport bus and a Persistence mechanism for the Saga States:

```csharp
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
        services.AddOpenSleigh(cfg =>{
            var rabbitSection = hostContext.Configuration.GetSection("Rabbit");
            var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"],
                rabbitSection["UserName"],
                rabbitSection["Password"]);

            cfg.UseRabbitMQTransport(rabbitCfg);

            var mongoSection = hostContext.Configuration.GetSection("Mongo");
            var mongoCfg = new MongoConfiguration(mongoSection["ConnectionString"],
                mongoSection["DbName"],
                MongoSagaStateRepositoryOptions.Default);

            cfg.UseMongoPersistence(mongoCfg);
        });
    });
```

In this example, the system is configured to use RabbitMQ as message bus and MongoDB to persist the data.

**IMPORTANT**: for detailed instructions on each Transport and Persistence mechanism, please refer to the library's documentation.

## Adding a Saga

A Saga is a simple class inheriting from the base [`Saga`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Saga.cs) class:

```csharp
public record MyAwesomeSagaState { }

public class MyAwesomeSaga : Saga
{
    private readonly ILogger<MyAwesomeSaga> _logger;

    public MyAwesomeSaga(ILogger<MyAwesomeSaga> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

Dependency injection can be used to provide services to a Saga.

Now, all you have to do is register and configure the Saga:

```csharp
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<MyAwesomeSaga>();
});
```

Sagas can also hold some _state_. In this case, we need to define its shape by creating a `class` or a `record` and setting it on the Saga:

```csharp
public record MySagaState
{
    public int Foo = 42;
    public string Bar = "71";
};

public class SagaWithState : Saga<MySagaState> {

}
```

Now you can register it this way:

```csharp
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<SagaWithState, MySagaState>();
});
```

The State can be accessed later on via `this.Context.State` and is also persisted automatically after a message is processed. For more details, check the [Handling Messages]({% link how-to/handling-messages.md %}) page.

**IMPORTANT**: each Saga should have its own State class. Don't reuse State classes!
