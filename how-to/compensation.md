---
layout: default
title: Compensation
description: how to configure Compensating Transactions in a Saga execution
---

# [How-to](/how-to/) / Compensation
There might be cases when failures in handling messages need to be handled properly. The first option would be to use a simple try/catch block. This would work but the code can get messy very quickly.

Another option, in case the error is transient (eg. a temporary network glitch), is to use a [Retry policy](/how-to/retry-policies.html).

The other alternative is to leverage a *Compensating Transaction*.

By adding the interface `ICompensateMessage<TMessage>` to our Saga class, OpenSleigh knows that in case any exception happens when processing a message of type `TMessage`, it has to execute some compensating operation.

```
public class MyAwesomeSaga :
    Saga<MyAwesomeSagaState>,    
    IHandleMessage<DoSomething>,
    ICompensateMessage<DoSomething>
{
    // code omitted for brevity

    public async Task HandleAsync(IMessageContext<ICompensateMessage> context, CancellationToken cancellationToken = default)
    {
       // something goes wrong here
    }

    public async Task CompensateAsync(ICompensationContext<ICompensateMessage> context, CancellationToken cancellationToken = default)
    {
       // handle the error here
    }
}
```

The `ICompensationContext<TMessage>` instance wraps the `IMessageContext<TMessage>` received previously and the `Exception` that was thrown:

```
public interface ICompensationContext<TM> where TM : IMessage
{
    IMessageContext<TM> MessageContext { get; }
    Exception Exception { get; }
}
```
