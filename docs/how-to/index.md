---
layout: default
---

# How-to
OpenSleigh is intended to be flexible and developer friendly. It makes use of Dependency Injection for its own initialization and the setup of the dependencies.

The first step, once you have installed the [Core library](https://www.nuget.org/packages/OpenSleigh.Core/), is to add OpenSleigh to the Services collection:

```
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
                services.AddOpenSleigh(cfg =>{ ... });
    });
```

## Configuring Transport and Persistence
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

**IMPORTANT**: for detailed instructions on each Transport and Persistence library, please refer to the specific README file located in the library's root folder.

## Spinning up the Infrastructure
OpenSleigh can also be used to initialize the infrastructure needed by your services. For example it's possible to automatically create Topics and Subscriptions when using the Azure Service Bus package.
This can be easily done by calling the `.SetupInfrastructureAsync()` extension method on `IHost`, OpenSleigh will take care of the rest:

```
var hostBuilder = CreateHostBuilder(args);
var host = hostBuilder.Build();

await host.SetupInfrastructureAsync();

await host.RunAsync();
```

**IMPORTANT**

However, we suggest to handle this separately, outside the scope of the application code. We believe that creating the infrastructure should be automated as much as possible and be treated as separate project, with its own deployment pipeline.

Moreover, it's often required to use specific permissions (eg. on connection strings), which might pose security issues and most certainly fall ouside the [Principle of least privilege](https://en.wikipedia.org/wiki/Principle_of_least_privilege).

Also, it's worth mentioning that OpenSleigh will use default values when creating the infrastructure, which might not exactly be what you're expecting.

That being said, it's certainly possible to leverage this functionality on non-PROD environments (eg. DEV or CI) to speed up the development process. Something like this:
```
if(!env.IsProduction())
    await host.SetupInfrastructureAsync();
```

## Adding a Saga

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

**IMPORTANT**: each Saga should have its own State class. Don't reuse State classes!

At this point all you have to do is register and configure the Saga:
```
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<MyAwesomeSaga, MyAwesomeSagaState>()      
        .UseRabbitMQTransport(rabbitConfig);
});
```

When adding a Saga, it's important to specify the Transport we want to use for its messages (inbound and outbound). In this example we're using the `UseRabbitMQTransport()` extension method, which tells OpenSleight to use RabbitMQ for this Saga.

## Retry Policies
Sagas can be configured to use a Retry Policy in case a message handler fails. Each message handler on a Saga can have its own policy:

```
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<MyAwesomeSaga, MyAwesomeSagaState>()      
        .UseRetryPolicy<StartSagaMessage>(builder => {
            builder.WithMaxRetries(5)
                .Handle<ApplicationException>()
                .WithDelay(executionIndex => TimeSpan.FromSeconds(executionIndex))
                .OnException(ctx =>
                {
                    System.Console.WriteLine(
                        $"tentative #{ctx.ExecutionIndex} failed: {ctx.Exception.Message}");
                });
        });
});
```

Calls to the `.Handle<>()` method can be chained to register multiple exception types.

## Starting a Saga
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

## Publishing messages
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

### Publish-only applications
An application can be also configured as *"publish-only"*: it will only take care of dispatching new messages but won't be able to consume any. Useful when creating a Web API which offloads the actual execution to a separate worker service.
