---
layout: default
title: Samples
---

# Samples
Sample projects are available in the [`/samples/` folder](https://github.com/mizrael/OpenSleigh/tree/develop/samples) of the repository. The required infrastructure for each sample can be spin up using *docker-compose* with the `.yml` configuration file provided in the sample's folder.

- **[Sample 1](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample1)** is a simple .NET Console Application showing how to bootstrap OpenSleigh and use In-Memory persistence and transport.

- **[Sample 2](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample2)** is a more interesting scenario, with a Web API acting as message producer and a Console Application as subscriber. This example uses RabbitMQ and MongoDB. It also uses a custom naming policy for some messages, which allows greater flexibility with exchanges and queue generation.

- **[Sample 3](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample3)** is the same as Sample 2, but with SQL Server instead of MongoDB.

- **[Sample 4](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample4)** mimicks an order processing scenario, showing how to orchestrate multiple services. It uses RabbitMQ and SQL Server.

- **[Sample 5](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample5)** shows how to configure retry policies on a Saga.

- **[Sample 6](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample6)** shows how to use Azure Service Bus as Transport mechanism and how to automatically provision the infrastructure.

- **[Sample 7](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample7)** shows how to configure *OpenSleigh* to use [compensating transactions](https://docs.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction?WT.mc_id=DOP-MVP-5003878).

- **[Sample 8](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample8)** shows how to send notifications to a Blazor client application from a Saga using SignalR.

- **[Sample 9](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample9)** same as sample 2 and 3, but with Kafka instead of RabbitMQ.

- **[Sample 10](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample10)** same as sample 2, but with PostgreSQL instead of SQL Server.

- **[Sample 11](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample11)** shows how to use Stateless Message Handlers with Kafka and PostgreSQL.

- **[Sample 12](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample12)** shows how to use Stateless Message Handlers with Azure ServiceBus.
