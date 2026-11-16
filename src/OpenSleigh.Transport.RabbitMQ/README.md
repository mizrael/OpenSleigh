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

### What a host declares on the broker

- A host declares the queue of its service, with its retry and dead letter queues, for every message type its sagas handle: when it starts consuming, and when it publishes one of them.
- For a message type it only publishes, a host declares the exchange and nothing else. This is the case of a host set to publish only (`SetPublishOnly()`), and of a service that publishes a message handled by the sagas of another service.
- A publish does not wait for the broker. A message that no queue takes, because no service that consumes its type has declared its queue yet, is sent back by the broker afterwards, and then:
  - it goes back to the outbox, with a Warning in the log once it is there (message id, type, exchange and the reason the broker gave), and is published again after `RetryDelay`, or after one second when `RetryDelay` is shorter;
  - this repeats for `ReturnedMessageRetryPeriod`, 5 minutes by default, counted from the first time the message came back. That time is stored with the message in the outbox and published with it (a `FirstReturnedAt` header): the period is the same for every instance, and survives a restart. A time that is earlier than the creation of the message, or later than the return itself, is not believed, and the period starts again;
  - once the period is over the message is never put back: it is published to the dead letter exchange of its type instead, with an Error in the log once it is. When that fails, and fails once more on a new channel, the message is dropped, with an Error in the log (message id, type, exchange and the failure). When the connection to the broker is down, the other messages handled with it (up to 100) whose period is over are dropped the same way without being tried;
  - a publisher declares no dead letter queue of its own. When no queue takes the message from the dead letter exchange either, it is dropped, and a second Error in the log (message id, type, exchange) is the only trace of it. With the default naming a dead letter queue takes it only when hosts of the same client group consume the type.

```
var rabbitCfg = new RabbitConfiguration(hostName, userName, password)
{
    ReturnedMessageRetryPeriod = TimeSpan.FromMinutes(30)
};
```

- With a period of zero a returned message is never put back, or published again: it goes to the dead letter exchange the first time it comes back.
- A message type that no service ever consumes is published about every `RetryDelay` for `ReturnedMessageRetryPeriod`, and then dropped as above.
- What this does not cover, each with an Error in the log that names the message:
  - a returned message is in the memory of the instance until it is back in the outbox. Up to 10,000 messages and 128 MB of message bodies can be waiting there, the one that comes back included: the ones that come back beyond that are lost, and so is a message whose body alone is larger. One instance stores about 1,100 to 1,800 returned messages a second on SQL Server and PostgreSQL, and 400 to 700 on MongoDB (1,300 when the message is still in the outbox), as measured on the build servers of the library: a publisher that floods a consumer that has never started with more than that for long enough fills the queue;
  - the messages are stored one by one. One that cannot be stored, whatever the reason (the outbox cannot be reached, or will never take it: a body or an id larger than the store allows), is tried again after the ones that are waiting, with a Warning in the log (message id, type and the failure) and 5 seconds after each failure, until its retry period is over: then it goes to the dead letter exchange as above. The bound is the period because it is the time the message has anyway, and the failure of one message says nothing about the next;
  - the same message returned twice at once (two relays published it) is handled twice: stored once, with the earliest time, or, when one of the two is past the period, also dead-lettered;
  - instances that share an outbox must agree on the time to well within the period: one whose clock is ahead ends the period of a message another one put back early;
  - when the application stops, the returned messages are stored once every hosted service has stopped, the outbox relay included, for as long as the host allows (`HostOptions.ShutdownTimeout`): the ones that are left then are lost. So is a message the broker returns after that, which is possible for the last ones the relay published;
  - every returned message is lost when the persistence registers no `IOutboxReturnRepository`. The persistence libraries of OpenSleigh register one; a custom one has to;
  - a publish that never reached the broker, because the connection was lost at that moment, still counts as published: there are no publisher confirms. This one leaves no trace.
- Rolling upgrade from 3.3.0: an instance of that version on the same outbox does not know the due time or the time of first return of a message that was put back. See the README of the persistence library: set `ReturnedMessageRetryPeriod` to zero until every instance is upgraded. With zero nothing is put back, whatever happens.
- In 3.3.0 and earlier such a message was kept by the broker in the queue the publisher declared for its own service. That was the queue of the consumers when they have the same client group as the publisher (a publish-only host of the same application, for instance) or when a custom naming policy gives a message type one queue: there, a message published before any consumer had started waited in the broker for as long as it took, and now waits in the outbox for `ReturnedMessageRetryPeriod`.

#### Queues left behind by 3.3.0 or earlier

In those versions a publisher also declared the queue of its own service for every message type it published, and bound it to the exchange of the type. When no host of that service handles the type nobody consumes from that queue, which receives a copy of every message and keeps it.

This version no longer declares those queues, and does not delete them either: a queue that exists stays bound and keeps receiving copies until it is removed. The library cannot tell such a queue from one that is consumed from, so removing them is left to the operator:

- only the queues of a client group that no consuming host uses are copies: the ones named after a service that publishes the message type and has no host that handles it (`<message type>.<client group>.workers` with the default naming). A queue whose client group is shared with hosts that handle the type is their queue, and must stay. With a custom naming policy, check which hosts consume from a queue before touching it;
- a queue of copies has no consumers and a number of messages that only grows; its retry queue (`<queue>.retry` or `<queue>.retry.direct`) and its dead letter queue (`<message type>.dead.<client group>.workers`) stay empty;
- once every instance of the service runs this version, delete the three of them and let the broker refuse when it is not safe: `rabbitmqctl delete_queue <queue> --if-unused` for the queue, which fails when the queue has consumers, and `rabbitmqctl delete_queue <name> --if-unused --if-empty` for its retry and dead letter queues, which fails when they hold messages. The messages in the queue itself are copies: the services that handle the message type received their own;
- an instance of an earlier version that is still running declares them again, so delete them after the upgrade, not during it.


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
