# OpenSleigh.Persistence.Cosmos.SQL
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.Cosmos.SQL?style=plastic)

## Description
CosmosDB persistence library for OpenSleigh using the SQL API

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.Cosmos.SQL/

## How-to
The first thing to do is build an instance of `CosmosSqlConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseCosmosPersistence` extension method:
```

services.AddOpenSleigh(cfg =>{ 
    var connectionString = Configuration.GetConnectionString("cosmos");
    var dbName = Configuration["cosmosDbName"];
    var cosmosCfg = new CosmosSqlConfiguration(connectionString, dbName);
    cfg.UseCosmosPersistence(cosmosCfg);
    
    // register the Sagas here
});
```

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.Persistence.InMemory/) or [Azure Service Bus](https://www.nuget.org/packages/OpenSleigh.Transport.AzureServiceBus/)).
