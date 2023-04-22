---
layout: default
title: Samples
---

# Samples
Sample projects are available in the [`/samples/` folder](https://github.com/mizrael/OpenSleigh/tree/develop/samples) of the repository. The required infrastructure for each sample can be spin up using *docker-compose* with the `.yml` configuration file provided in the sample's folder.

- **[Sample 1](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample1)** is a simple .NET Console Application showing how to bootstrap OpenSleigh and use In-Memory persistence and transport.

- **[Sample 2](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample2)** is a more interesting scenario, with a Web API acting as message producer and a Console Application as subscriber. This example uses RabbitMQ and MongoDB. 