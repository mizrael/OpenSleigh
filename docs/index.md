---
layout: default
title: Home
nav_order: 1
permalink: /
---

# OpenSleigh

![OpenSleigh](assets/images/opensleigh-banner.png)

**OpenSleigh** is a distributed Saga management library, written in C# with .NET Core. It is intended to be reliable, fast, easy to use, configurable, and extensible.

## What?

So what is a Saga exactly? The basic idea is quite interesting: in a micro-service architecture, it often happens that we need to manage several long-running operations that span multiple services.

A good example could be an Order processing workflow: in this scenario, you have to orchestrate multiple services, do inventory management, credit check, handle shipping, and so on.

**OpenSleigh** helps by taking care of [distributed transactions](https://www.davidguida.net/improving-microservices-reliability-part-1-two-phase-commit/), keeping track of the global status, and managing the whole flow.

For more details, check the [Use Cases]({% link use-cases.md %}) page.

## Installation

The Core module is available [on Nuget](https://www.nuget.org/packages/OpenSleigh). However, Transport and Persistence packages are necessary to properly use the library.

These are the packages available at the moment:

| Package | NuGet |
|---------|-------|
| [Core](https://www.nuget.org/packages/OpenSleigh/) | ![Nuget](https://img.shields.io/nuget/v/OpenSleigh?style=flat-square) |
| [In-Memory](https://www.nuget.org/packages/OpenSleigh.InMemory/) | ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.InMemory?style=flat-square) |
| [MongoDB](https://www.nuget.org/packages/OpenSleigh.Persistence.Mongo/) | ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.Mongo?style=flat-square) |
| [SQL Server](https://www.nuget.org/packages/OpenSleigh.Persistence.SQLServer/) | ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.SQLServer?style=flat-square) |
| [PostgreSQL](https://www.nuget.org/packages/OpenSleigh.Persistence.PostgreSQL/) | ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.PostgreSQL?style=flat-square) |
| [RabbitMQ](https://www.nuget.org/packages/OpenSleigh.Transport.RabbitMQ/) | ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Transport.RabbitMQ?style=flat-square) |
| [Kafka](https://www.nuget.org/packages/OpenSleigh.Transport.Kafka/) | ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Transport.Kafka?style=flat-square) |

In-depth instructions can be found in the [How-To]({% link how-to/installation.md %}) section.

## Issues? Questions? Suggestions?

Feel free to [reach out](https://github.com/mizrael/OpenSleigh/discussions) and tell us what you think!
