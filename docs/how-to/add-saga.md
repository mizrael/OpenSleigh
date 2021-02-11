---
layout: default
---

# Adding a Saga

A Saga is a simple class inheriting from the base [`Saga<>`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh.Core/Saga.cs) class. We also have to create an additional State class holding it's data, by inheriting from [`SagaState`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh.Core/SagaState.cs):

```
public class MyAwesomeSagaState : SagaState{
    public MyAwesomeSagaState(Guid id) : base(id){}
}

public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>
{
    private readonly ILogger<MyAwesomeSaga> _logger;       

    public ParentSaga(ILogger<MyAwesomeSaga> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

Dependency injection can be used to reference services from Sagas.

**IMPORTANT**: each Saga should have its own State class. Don't reuse State classes!

At this point all you have to do is register and configure the Saga:
```
services.AddOpenSleigh(cfg =>{
    cfg.AddSaga<MyAwesomeSaga, MyAwesomeSagaState>()      
        .UseRabbitMQTransport(rabbitConfig);
});
```

When adding a Saga, it's important to specify the Transport we want to use for its messages (inbound and outbound). In this example we're using the `UseRabbitMQTransport()` extension method, which tells OpenSleight to use RabbitMQ for this Saga.
