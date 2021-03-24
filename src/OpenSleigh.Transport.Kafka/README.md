# OpenSleigh.Transport.Kafka
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Transport.Kafka?style=plastic)

## Description
Kafka Transport library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Transport.Kafka/

## How-to

The first thing to do is build an instance of `KafkaConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseKafkaTransport` extension method:

```
services.AddOpenSleigh(cfg =>{     
    var connectionString = Configuration.GetConnectionString("kafka");
    var consumerGroup = Configuration["consumerGroup"];
    var kafkaConfig = new KafkaConfiguration(connectionString, consumerGroup);

    cfg.UseKafkaTransport(kafkaConfig);

    // register the Persistence and the Sagas
});
```

It is also possible to use a custom naming policy to customize the name of the Topic and the Dead Letter Queue:

```
services.AddOpenSleigh(cfg =>{  
    // code omitted
    cfg.UseKafkaTransport(kafkaConfig, builder =>
    {                        
        builder.UseMessageNamingPolicy<ProcessOrder>(() => new QueueReferences("orders", "orders.dead"));
    })
});
```

If your application has to handle messages and events, not just dispatch them, you also have to configure each Saga :

```
services.AddOpenSleigh(cfg =>{  
    // code omitted //

    cfg.AddSaga<MySaga, MySagaState>()
        .UseStateFactory<StartSaga>(msg => new MySagaState(msg.CorrelationId))
        .UseKafkaTransport();
});
```