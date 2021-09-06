# OpenSleigh.Persistence.PostgreSQL
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.PostgreSQL?style=plastic)

## Description
PostgreSQL Persistence library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.PostgreSQL/

## How-to
The first thing to do is build an instance of `SqlConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `SqlBusConfiguratorExtensions` extension method:
```

services.AddOpenSleigh(cfg =>{ 
    var connectionString = Configuration.GetConnectionString("sql");
    var sqlCfg = new SqlConfiguration(connectionString);
    cfg.SqlBusConfiguratorExtensions(sqlCfg);
    
    // register the Sagas here
});
```

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.Persistence.InMemory/) or [Azure Service Bus](https://www.nuget.org/packages/OpenSleigh.Transport.AzureServiceBus/)).
