---
layout: default
title: Configure Transport and Persistence
description: how to configure Transport and Persistence libraries, for example with RabbitMQ and MongoDB
---

# [How-to](/how-to/) / Configure Transport and Persistence
OpenSleigh needs to be configured to point to a specific Transport bus and a Persistence mechanism for the Saga States:
```
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
                services.AddOpenSleigh(cfg =>{ 
            var rabbitSection = hostContext.Configuration.GetSection("Rabbit");
            var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"], 
                rabbitSection["UserName"],
                rabbitSection["Password"]);

            gfg.UseRabbitMQTransport(rabbitCfg);

            var mongoSection = hostContext.Configuration.GetSection("Mongo");
            var mongoCfg = new MongoConfiguration(mongoSection["ConnectionString"],
                mongoSection["DbName"],
                MongoSagaStateRepositoryOptions.Default);

            cfg.UseMongoPersistence(mongoCfg);
        });
    });
```
In this example, the system is configured to use RabbitMQ as message bus and MongoDB to persist the data.

**IMPORTANT**: for detailed instructions on each Transport and Persistence library, please refer to the specific README file located in the library's root folder.