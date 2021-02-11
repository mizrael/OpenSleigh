---
layout: default
---

# Retry Policies
Sagas can be configured to use a Retry Policy in case a message handler fails. Each message handler on a Saga can have its own policy:

```
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<MyAwesomeSaga, MyAwesomeSagaState>()      
        .UseRetryPolicy<StartSagaMessage>(builder => {
            builder.WithMaxRetries(5)
                .Handle<ApplicationException>()
                .WithDelay(executionIndex => TimeSpan.FromSeconds(executionIndex))
                .OnException(ctx =>
                {
                    System.Console.WriteLine(
                        $"tentative #{ctx.ExecutionIndex} failed: {ctx.Exception.Message}");
                });
        });
});
```

Calls to the `.Handle<>()` method can be chained to register multiple exception types.