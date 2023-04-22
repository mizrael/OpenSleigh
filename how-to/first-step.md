---
layout: default
title: First step
---

# [How-to](/how-to/) / First step
OpenSleigh is intended to be flexible and developer friendly. It makes use of Dependency Injection for its own initialization and the setup of the dependencies.

The first step, once you have installed the [Core library](https://www.nuget.org/packages/OpenSleigh.Core/), is to add OpenSleigh to the Services collection:

```
Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => {
                services.AddOpenSleigh(cfg =>{ ... });
    });
```