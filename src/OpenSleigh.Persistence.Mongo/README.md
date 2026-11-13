# OpenSleigh.Persistence.Mongo
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.Mongo?style=plastic)

## Description
MongoDB Persistence library for OpenSleigh.

For more details, check the [Official Documentation](https://opensleigh.gitbook.io/).

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

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.InMemory/) or [RabbitMQ](https://www.nuget.org/packages/OpenSleigh.Transport.RabbitMQ/)).

## Upgrading from version 3.3.0 or earlier

Delayed messages are stored with a `DeliverAt` date on their outbox document; messages without a delay are stored as before. The outbox collection gets one more index, `ix_deliver_at`, created at startup like the others: building it on a large outbox takes time, and the database user needs the `createIndex` action, as for the existing indexes. Nothing has to be done by hand.

Instances of the earlier version do not know about delays and publish a delayed message as soon as they read it: do not publish messages with a delay until every instance runs the new version.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)
