# OpenSleigh.Transport.RabbitMQ
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Transport.RabbitMQ?style=plastic)

## Description
RabbitMQ Transport library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Transport.RabbitMQ/

## How-to

The first thing to do is build an instance of `RabbitConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseRabbitMQTransport` extension method:

```
services.AddOpenSleigh(cfg =>{     
    var rabbitSection = Configuration.GetSection("Rabbit");
    var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"],
        rabbitSection["UserName"],
        rabbitSection["Password"]);

        cfg.UseRabbitMQTransport(rabbitCfg);

    // register the Persistence and the Sagas
});
```

It is also possible to use a custom naming policy to define the names for exchanges and queues. This allows us to have a single exchange binded to multiple queues. Messages will be routed using the queue name.

```
services.AddOpenSleigh(cfg =>{  
    // code omitted
    cfg.UseRabbitMQTransport(rabbitCfg, builder =>
    {                        
        builder.UseMessageNamingPolicy<StartChildSaga>(() => new QueueReferences("child", "child.start", "child.dead", "child.dead.start"));
        builder.UseMessageNamingPolicy<ProcessChildSaga>(() => new QueueReferences("child", "child.process", "child.dead", "child.dead.process"));
    })
});

```


If your application has to handle messages and events, not just dispatch them, you also have to configure each Saga :

```
services.AddOpenSleigh(cfg =>{  
    // code omitted //

    cfg.AddSaga<MySaga, MySagaState>()
        .UseStateFactory<StartSaga>(msg => new MySagaState(msg.CorrelationId))
        .UseRabbitMQTransport();
});
```