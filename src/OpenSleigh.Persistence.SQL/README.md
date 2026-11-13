# OpenSleigh.Persistence.SQL

# This package should not be used directly, unless you are intentionally using it to create a new SQL persistence layer. 

The following providers are already available:
- SQL Server: ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.SQLServer?style=plastic)
- PostgreSQL: ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.PostgreSQL?style=plastic)

## Notes for a new provider

- Register the context with `services.AddPooledSagaDbContext(builder => ...)`. Every consumer still gets a `SagaDbContext` of its own, but contexts are reused once disposed instead of being built for every message.
- `SqlOutboxRepository` implements `IOutboxBatchRepository`, so the outbox relay deletes what it published in a pass with one round trip: one statement by default, which a provider can replace by overriding `DeleteBatchAsync` (SQL Server deletes by key). The relay reads, publishes and deletes inside one transaction, on the context of `SqlTransactionManager`: pass it to the repository as `relayDbContext`.
- Every outbox row has a due time, `DeliverAt`: `OutboxMessage.DueImmediately` (1970-01-01 UTC) for a message without a delay, which is also the default of the column. `ReadPendingAsync` must only return rows whose due time is not later than now, and should find them through the index on the column (`OutboxMessage.DeliverAtIndexName`), so that rows that are not due cost nothing.
- A database created by a version before the `DeliverAt` column needs it added. Derive from `SagaDbSchemaUpgrade` (a query that tells whether the schema is current, a statement that makes concurrent upgrades wait for each other, and statements that do nothing when there is nothing to add) and register it as `SagaDbSchemaUpgrade`: `SagaDbSchemaInitializer` applies it to a database that already existed, once per service provider.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)