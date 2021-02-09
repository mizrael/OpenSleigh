# OpenSleigh.Transport.AzureServiceBus
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Transport.AzureServiceBus?style=plastic)

## Description
Azure Service Bus Transport library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Transport.AzureServiceBus/

## How-to

The first thing to do is build an instance of `AzureServiceBusConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseAzureServiceBusTransport` extension method:

```
services.AddOpenSleigh(cfg =>{     
    var connStr = Configuration.GetConnectionString("AzureServiceBus");
    var config = new AzureServiceBusConfiguration(connStr);
    cfg.UseAzureServiceBusTransport(config);

    // register the Persistence and the Sagas
});
```

It is also possible to use a custom naming policy to define the names for topics and subscriptions. This opens the door to multiple, different scenarios (eg. a single topic with multiple subscriptions, one per message). 

```
services.AddOpenSleigh(cfg =>{  
    // code omitted
    cfg.UseAzureServiceBusTransport(config, builder =>
    {                        
        builder.UseMessageNamingPolicy<StartParentSaga>(() =>
                        new QueueReferences("my-topic", "start-parent-saga"));
        builder.UseMessageNamingPolicy<ProcessParentSaga>(() =>
                        new QueueReferences("my-topic", "process-parent-saga"));
    })
});

```

If your application has to handle messages and events, not just dispatch them, you also have to configure each Saga :

```
services.AddOpenSleigh(cfg =>{  
    // code omitted //

    cfg.AddSaga<MySaga, MySagaState>()
        .UseStateFactory<StartSaga>(msg => new MySagaState(msg.CorrelationId))
        .UseAzureServiceBusTransport();
});
```