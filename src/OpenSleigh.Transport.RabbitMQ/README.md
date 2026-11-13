# OpenSleigh.Transport.RabbitMQ
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Transport.RabbitMQ?style=plastic)

## Description
RabbitMQ Transport library for OpenSleigh.

For more details, check the [Official Documentation](https://opensleigh.gitbook.io/).

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

It is also possible to use a custom naming policy to define the names for exchanges and queues. This allows us to have a single exchange bound to multiple queues. Messages will be routed using the queue name.

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

### Consumer throughput

By default a consumer handles one message at a time, in the order the broker delivers them, and the broker pushes messages without limit. Two settings on `RabbitConfiguration` change that:

```
var rabbitCfg = new RabbitConfiguration(hostName, userName, password)
{
    ConsumerDispatchConcurrency = 16,
    PrefetchCount = 64
};
```

- `ConsumerDispatchConcurrency` is how many messages are handled at the same time. Raising it raises throughput, but messages are no longer handled in delivery order, and messages for the same saga instance that arrive together contend for its lock and go through the retry queue.
- `PrefetchCount` is how many unacknowledged messages the broker hands to a consumer at once (0 means no limit). Set it when several workers share a queue, to a value at least as large as `ConsumerDispatchConcurrency`.
### When a message cannot be processed

- When the saga instance a message is for is being processed by another worker (a lock conflict), the message goes through the retry queue of the service and is processed again after `RetryDelay`, 30 seconds by default. This repeats for as long as the conflict lasts. Only the service that could not process the message gets it again, not the other services that handle the same message type.
- On any other failure, a saga handler that throws included, the message is rejected and ends up in the dead letter queue. It is not processed again.
- A message whose processing is interrupted because the application is stopping is neither acknowledged nor rejected: the broker delivers it again once the connection is closed.

#### Upgrading from 3.3.0 or earlier

With the default naming, the retry queue of those versions (`<queue>.retry`) sends a message back to every service that handles the message type, and a queue cannot be told otherwise once it exists. This version declares another one next to it, `<queue>.retry.direct`, and leaves the old one alone:

- nothing has to be deleted or migrated, and old and new instances can run side by side;
- messages waiting in the old queue during the upgrade are still delivered, to every service as before;
- instances that have not been upgraded keep retrying the old way until they are;
- once every instance is upgraded the old queue stays empty, and can be deleted.

When the routing key of a message type is the name of its queue (a custom naming policy), the retry queue already returned its messages to that queue only, and it is kept as it is.


If your application has to handle messages and events, not just dispatch them, you also have to configure each Saga :

```
services.AddOpenSleigh(cfg =>{  
    // code omitted //

    cfg.AddSaga<MySaga, MySagaState>()
        .UseStateFactory<StartSaga>(msg => new MySagaState(msg.CorrelationId))
        .UseRabbitMQTransport();
});
```

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)
