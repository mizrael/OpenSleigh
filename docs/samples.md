---
layout: default
---

# Samples
Samples are available in the `/samples/` folder. The required infrastructure for each sample can be spin up using *docker-compose* with the `.yml` configuration file provided in the sample's folder.

- **[Sample1](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample1)** is a simple .NET Console Application showing how to bootstrap OpenSleigh and use In-Memory persistence and transport.

- **[Sample2](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample2)** is a more interesting scenario, with a Web API acting as message producer and a Console Application as subscriber. This example uses RabbitMQ and MongoDB. It also uses a custom naming policy for some messages, which allows greater flexibility with exchanges and queue generation.

- **[Sample3](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample3)** is the same as Sample2, but with SQL Server instead of MongoDB.

- **[Sample4](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample4)** mimicks an order processing scenario, showing how to orchestrate multiple services. It uses RabbitMQ and SQL Server.

- **[Sample5](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample5)** shows how to configure retry policies on a Saga.

- **[Sample6](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample6)** shows how to use Azure Service Bus as Transport mechanism and how to automatically provision the infrastructure.

