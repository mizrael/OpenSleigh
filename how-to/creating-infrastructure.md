---
layout: default
title: Spinning up the Infrastructure
---

# [How-to](/how-to/) / Spinning up the Infrastructure
OpenSleigh can also be used to initialize the infrastructure needed by your services. For example it's possible to automatically create Topics and Subscriptions when using the Azure Service Bus package.
This can be easily done by calling the `.SetupInfrastructureAsync()` extension method on `IHost`, OpenSleigh will take care of the rest:

```
var hostBuilder = CreateHostBuilder(args);
var host = hostBuilder.Build();

await host.SetupInfrastructureAsync();

await host.RunAsync();
```

**IMPORTANT**

However, we suggest to handle this separately, outside the scope of the application code. We believe that creating the infrastructure should be automated as much as possible and be treated as separate project, with its own deployment pipeline.

Moreover, it's often required to use specific permissions (eg. on connection strings), which might pose security issues and most certainly fall ouside the [Principle of least privilege](https://en.wikipedia.org/wiki/Principle_of_least_privilege).

Also, it's worth mentioning that OpenSleigh will use default values when creating the infrastructure, which might not exactly be what you're expecting.

That being said, it's certainly possible to leverage this functionality on non-PROD environments (eg. DEV or CI) to speed up the development process. Something like this:
```
if(!env.IsProduction())
    await host.SetupInfrastructureAsync();
```