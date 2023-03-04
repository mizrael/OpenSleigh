# OpenSleigh.Persistence.Mongo
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.Mongo?style=plastic)

## Description
MongoDB Persistence library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.Mongo/

## How-to
The first thing to do is build an instance of `MongoConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseMongoPersistence` extension method:
```

services.AddOpenSleigh(cfg =>{ 
    var mongoSection = Configuration.GetSection("Mongo");
    var mongoCfg = new MongoConfiguration(mongoSection["ConnectionString"],
        mongoSection["DbName"]);
    cfg.UseMongoPersistence(mongoCfg);
    
    // register the Sagas here
});
```

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.Persistence.InMemory/) or [RabbitMQ](https://www.nuget.org/packages/OpenSleigh.Transport.RabbitMQ/)).

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)