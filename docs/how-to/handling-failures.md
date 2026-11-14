---
layout: default
title: Handling Failures
parent: How-To
nav_order: 4
---

# Handling Failures

When a message handler throws, OpenSleigh does three things:

1. It discards everything the handler did to the Saga: state changes, the completed flag, and the messages it published.
2. It runs the Saga's compensation handler for that message, if there is one. Otherwise the message is dead-lettered.
3. It stores a record of the failure and of what was decided.

A Saga is only written when a handler, or a compensation handler, succeeds.

{: .note }
A handler that fails is not retried in this version. Retry policies are planned for the same major release.

## Compensating a failed message

To react to the failure of a handler, implement [`ICompensateMessage<>`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Transport/ICompensateMessage.cs) for the same message type:

```csharp
public class OrderSaga :
    Saga<OrderState>,
    IStartedBy<PlaceOrder>,
    IHandleMessage<ChargeCard>,
    ICompensateMessage<ChargeCard>
{
    // code omitted for brevity

    public async ValueTask HandleAsync(
        IMessageContext<ChargeCard> context,
        CancellationToken cancellationToken = default)
    {
        Context.State.Charged = true;
        await _payments.ChargeAsync(context.Message.Amount, cancellationToken);
    }

    public ValueTask CompensateAsync(
        IMessageContext<ChargeCard> context,
        MessageFailure failure,
        CancellationToken cancellationToken = default)
    {
        Context.State.Cancelled = true;
        Publish(new CancelOrder(failure.Exception.Message));
        return ValueTask.CompletedTask;
    }
}
```

- The compensation handler sees the Saga state as it was **before** the failed handler ran. In the example, `Charged` is `false` again.
- `MessageFailure` carries the exception the handler threw and how many times the handler ran.
- When the compensation handler returns, its state changes and the messages it published are stored, and the message counts as processed. It is not dead-lettered and not delivered again.
- When the compensation handler throws, nothing of it is stored and the message is dead-lettered. A compensation is not tried again.
- A Saga can only compensate messages it handles. Registering one that does not fails at start-up.

Only the Saga is restored. Whatever the failed handler did outside of it, such as a call to another service, is for the compensation handler to undo.

## Dead-lettered messages

A message is dead-lettered when its handler fails and the Saga has no compensation handler for it, or when the compensation handler fails too. Where it goes depends on the transport:

| Transport | Destination |
|-----------|-------------|
| RabbitMQ | the dead letter queue of the message type and service |
| Kafka | the dead letter topic of the message type |
| In-memory | `InMemoryDeadLetters`, registered as a singleton, which keeps the last 1,000 |

When several Sagas handle the same message, each one gets its turn. One that fails does not stop the others, and the message is dead-lettered once.

## What does not count as a failure

- **A lock conflict.** Another worker was processing the same Saga instance. The handler did not run, and the transport delivers the message again after a delay.
- **A stop.** A handler interrupted because the application is shutting down is not a failure. Nothing is recorded and the message is delivered again.

## Failure records

Every failed message is recorded with:

| Field | Content |
|-------|---------|
| `InstanceId`, `CorrelationId`, `SagaType` | which Saga |
| `MessageId`, `MessageType` | which message |
| `Attempt` | 1 for the first failure |
| `OccurredAt` | when, in UTC |
| `ExceptionType`, `ExceptionMessage`, `StackTrace` | the exception the handler threw; the stack trace is cut at 4,000 characters |
| `Decision` | `Compensated`, `CompensationFailed` or `DeadLettered` |
| `CompensationError` | why the compensation did not go through, for `CompensationFailed`; cut at 4,000 characters |

Where they are stored:

| Persistence | Storage |
|-------------|---------|
| SQL Server, PostgreSQL | the `SagaMessageFailures` table, in the same transaction that releases the Saga |
| MongoDB | the `sagaMessageFailures` collection, written before the Saga is released |
| In-memory | a list of the last 1,000 |

To read them, inject [`ISagaFailureQuery`](https://github.com/mizrael/OpenSleigh/blob/develop/src/OpenSleigh/Queries/ISagaFailureQuery.cs), or use the [reporting endpoints]({% link how-to/reporting.md %}):

```csharp
var failures = await failureQuery.GetAllAsync(new SagaFailureQueryFilter
{
    InstanceId = instanceId,
    Decision = FailureDecision.DeadLettered
});
```

### Removing old records

Nothing removes failure records automatically. To remove the ones older than a given time, inject `ISagaFailurePurger`:

```csharp
var removed = await purger.PurgeAsync(DateTimeOffset.UtcNow.AddDays(-30));
```

On MongoDB the server can also expire them:

```csharp
cfg.UseMongoPersistence(new MongoConfiguration(connectionString, dbName)
{
    FailureRecordsExpireAfter = TimeSpan.FromDays(30)
});
```

## Upgrading from version 3

### `RollbackAsync` is gone

`IHandleMessage<TM>.RollbackAsync` no longer exists. Move its code to `ICompensateMessage<TM>.CompensateAsync`.

- A Saga that still declares `RollbackAsync(IMessageContext<TM>, CancellationToken)` is refused when it is registered, with an `InvalidOperationException` that names the method. Without that check the method would compile and never run.
- The code behaves differently once moved: it sees the state from before the failed handler, it receives the failure, what it publishes is delivered, and when it returns the message is processed instead of dead-lettered.

### A failed handler no longer changes the Saga

In version 3 the Saga was stored as the failed handler left it. Now it stays as it was before the message.

### In-memory state must be serializable

The in-memory persistence now keeps the state serialized, as the other stores do, and builds an instance for every message. The state type has to survive a round trip through the configured `ISerializer`, and a Saga no longer receives the same state object from one message to the next.

### Database changes

- **SQL Server, PostgreSQL:** the `SagaMessageFailures` table is created when the application starts, if it is missing. The database user needs the right to create a table once: `CREATE TABLE` and `ALTER` on the `opensleigh` schema on SQL Server, `CREATE` on the schema on PostgreSQL. A user without it is told which statements to run. When another user creates the table, grant the application user `SELECT`, `INSERT` and `DELETE` on it.
- **MongoDB:** the `sagaMessageFailures` collection and its indexes are created when the application starts.

### Changed interfaces

For code that implements or replaces parts of the library:

- `ISagaStateRepository` has a new member, `AbandonAsync`, which releases a Saga without storing it. `ReleaseAsync` and `ISagaStateCommitter.CommitAsync` also store the failure record of the instance (`ISagaInstance.Failure`).
- `ISagaInstance` has new members: `Failure`, `RecordFailure` and `HandLockTo`.
- `ISagaExecutionService.ReleaseAsync` is replaced by `AbandonAsync`, and `HandleFailureAsync` is new.
- `IMessageHandlerManager` has a new member, `CompensateAsync`.
- `OpenSleigh.Persistence.Mongo.IDbContext` has a new member, `SagaMessageFailures`.
