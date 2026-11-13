# OpenSleigh.InMemory
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.InMemory?style=plastic)

## Description
In-memory Persistence and Transport library for OpenSleigh.

For more details, check the [Official Documentation](https://opensleigh.gitbook.io/).

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.InMemory/

## How-to
We can use this library for both Persistence and Transport:

```
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
            services.AddOpenSleigh(cfg =>{ 
                cfg.UseInMemoryTransport()
                    .UseInMemoryPersistence();

            // register the Sagas here
         });
    });
```

Of course it can be used in conjunction with any other library as well (eg. InMemory Transport + MongoDb Persistence).

### When a message cannot be processed

- When the saga instance a message is for is being processed at the same time (a lock conflict), the message is processed again after a delay, 100 milliseconds by default. This repeats for as long as the conflict lasts.
- On any other failure the message is not processed again. It is kept, with the exception, in `InMemoryDeadLetters`, a singleton that holds the last 1000 of them:

```
cfg.UseInMemoryTransport(new InMemorySagaOptions(5) { SubscriberRetryDelay = TimeSpan.FromMilliseconds(500) });

// later, for instance in a test
var deadLetters = host.Services.GetRequiredService<InMemoryDeadLetters>().Messages;
```

If you decide to use the InMemory Transport, you also have to configure each Saga :


```
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
            // code omitted //
            cfg.AddSaga<MySaga, MySagaState>()
                    .UseStateFactory<StartSaga>(msg => new MySagaState(msg.CorrelationId))
                    .UseInMemoryTransport();
         });
    });
```

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)
