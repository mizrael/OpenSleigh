# OpenSleigh.Persistence.InMemory
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.InMemory?style=plastic)

## Description
In-memory Persistence and Transport library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.InMemory/

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
