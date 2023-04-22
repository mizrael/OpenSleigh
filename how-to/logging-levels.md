---
layout: default
title: Logging levels
description: how to configure Logging levels in OpenSleigh
---

# [How-to](/how-to/) / Logging levels

Logging levels in **OpenSleigh** can be easily configured by tweaking the default .NET Core `Logging` configuration node. 

For example, this configuration will log every possible message emitted by the library:

```
{
  "Logging": {
    "LogLevel": {
      "Default": "Error",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "OpenSleigh": "Trace"
    }
  }
}
```

It is of course possible to customize the levels even more, by adding more sub-namespaces: 

```
{
  "Logging": {
    "LogLevel": {      
      "OpenSleigh": "Error",
      "OpenSleigh.Persistence": "Information",
      "OpenSleigh.Transport": "Warning"
    }
  }
}
```
