# OpenSleigh.Persistence.Cosmos.Mongo
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.Cosmos.Mongo?style=plastic)

## Description
CosmosDB Persistence driver for OpenSleigh using the MongoDB API

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.Cosmos.Mongo/

## How-to
The first thing to do is build an instance of `CosmosConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseCosmosPersistence` extension method:
```

services.AddOpenSleigh(cfg =>{ 
    var connectionString = Configuration.GetConnectionString("cosmos");
    var dbName = Configuration["cosmosDbName"];
    var cosmosCfg = new CosmosConfiguration(connectionString, dbName);
    cfg.UseCosmosPersistence(cosmosCfg);
    
    // register the Sagas here
});
```

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.Persistence.InMemory/) or [Azure Service Bus](https://www.nuget.org/packages/OpenSleigh.Transport.AzureServiceBus/)).

### Notes
- as of today, Cosmos has no support for multi-document transactions across collections.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)