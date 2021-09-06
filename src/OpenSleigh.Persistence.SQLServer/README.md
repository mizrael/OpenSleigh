# OpenSleigh.Persistence.SQLServer
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.SQLServer?style=plastic)

## Description
SQL Server Persistence library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.SQLServer/

## How-to
The first thing to do is build an instance of `SqlConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseSqlServerPersistence` extension method:
```

services.AddOpenSleigh(cfg =>{ 
    var connectionString = Configuration.GetConnectionString("sql");
    var sqlCfg = new SqlConfiguration(connectionString);
    cfg.UseSqlServerPersistence(sqlCfg);
    
    // register the Sagas here
});
```

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.Persistence.InMemory/) or [Azure Service Bus](https://www.nuget.org/packages/OpenSleigh.Transport.AzureServiceBus/)).
