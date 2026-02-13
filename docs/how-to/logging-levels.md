---
layout: default
title: Logging Levels
parent: How-To
nav_order: 8
---

# Logging Levels

Logging levels in **OpenSleigh** can be easily configured by tweaking the default .NET Core `Logging` configuration node.

For example, this configuration will log every possible message emitted by the library:

```json
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

```json
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
