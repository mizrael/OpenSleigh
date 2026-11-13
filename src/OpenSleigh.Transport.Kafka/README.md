# OpenSleigh.Transport.Kafka
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Transport.Kafka?style=plastic)

## Description
Kafka Transport library for OpenSleigh. 

For more details, check the [Official Documentation](https://opensleigh.gitbook.io/).

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Transport.Kafka/

## How-to

The first thing to do is build an instance of `KafkaConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseKafkaTransport` extension method:

```
services.AddOpenSleigh(cfg =>{     
    var connectionString = Configuration.GetConnectionString("kafka");
    var kafkaConfig = new KafkaConfiguration(connectionString);

    cfg.UseKafkaTransport(kafkaConfig);

    // register the Persistence and the Sagas
});
```

The producer sends every message as soon as it is published. If something in your application publishes many messages at once through the same producer and you would rather trade latency for larger requests, set `ProducerLinger` (Kafka's `linger.ms`):

```
var kafkaConfig = new KafkaConfiguration(connectionString) { ProducerLinger = TimeSpan.FromMilliseconds(5) };
```

### When a message cannot be processed

- When the saga instance a message is for is being processed by another worker (a lock conflict), the message is published to the retry topic of its type and processed again after `RetryDelay`, which defaults to 30 seconds. This repeats for as long as the conflict lasts. The messages that follow are not kept waiting, so a retried message is processed after messages that were published later.
- On any other failure the message is published to the dead letter topic, with the reason in the `error` header, and is not processed again.

```
var kafkaConfig = new KafkaConfiguration(connectionString) { RetryDelay = TimeSpan.FromSeconds(5) };
```

The retry topic is named after the topic, followed by `.retry`; set `RetryTopicName` on `QueueReferences` to change it. It is shared by every service that handles the message type: a message in it carries the client group that asked for the retry, and the other groups skip it. The consumer asks the broker to create the topics it subscribes to, which works when the broker allows it (`auto.create.topics.enable`). If yours does not, create the retry topics along with the others. Creating them up front is the better choice anyway: a topic that appears after the consumer has started is assigned to it in a second rebalance, a moment later.

A message is marked as consumed only after it has been processed, or published to its retry or dead letter topic, so:

- a message that was being processed, or was waiting for its retry, when the application stopped or lost its partition is delivered again;
- a message that could be published to neither topic is not skipped: it is processed again after `RetryDelay`, and nothing else is read from its partition until it is dealt with;
- a message that cannot be read at all (an unknown type, a missing header) is logged and skipped.

Offsets are stored for the last message handled of each partition: every 100 messages, whenever the consumer has nothing to read, before a partition is given up in a rebalance, and when the consumer stops. Storing one for every message costs about a tenth of the consumer's throughput. If the process dies in between, the messages handled since are delivered again: sagas ignore the ones they have recorded as processed.

A custom `IConsumerBuilderFactory` has to set `enable.auto.offset.store` to `false` to keep this; when it does not, the subscriber logs a warning and messages in flight at a stop are lost. One that sets its own rebalance handlers gets a warning too: messages waiting for a retry can then be processed twice after a rebalance.

The id of a message, as a saga sees it, is its key as Kafka delivers it: the id it was published with, between double quotes. That is how every version so far has written it, and sagas have stored those ids as processed, so it stays.

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

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)
