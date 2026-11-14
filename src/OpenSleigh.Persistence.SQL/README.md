# OpenSleigh.Persistence.SQL

# This package should not be used directly, unless you are intentionally using it to create a new SQL persistence layer. 

The following providers are already available:
- SQL Server: ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.SQLServer?style=plastic)
- PostgreSQL: ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.PostgreSQL?style=plastic)

## Notes for a new provider

- Register the context with `services.AddPooledSagaDbContext(builder => ...)`. Every consumer still gets a `SagaDbContext` of its own, but contexts are reused once disposed instead of being built for every message.
- `SqlOutboxRepository` implements `IOutboxBatchRepository`, so the outbox relay deletes what it published in a pass with one round trip: one statement by default, which a provider can replace by overriding `DeleteBatchAsync` (SQL Server deletes by key). The relay reads, publishes and deletes inside one transaction, on the context of `SqlTransactionManager`: pass it to the repository as `relayDbContext`.
- An outbox row has a due time, `DeliverAt`, when its message was published with a delay, and none otherwise. `ReadPendingQuery` is the statement a provider reads the outbox with: it must return the delayed rows that are due and the rows without a due time, each kind having its share of a pass so that neither keeps the other waiting, and it should find both through the index on the column (`OutboxMessage.DeliverAtIndexName`), so that rows that are not due cost nothing. A provider written for an earlier version overrode `ReadPendingAsync`: it now has to supply `ReadPendingQuery`.
- A database created by a version before the `DeliverAt` column needs it added. Derive from `SagaDbSchemaUpgrade`: it lists the changes (`SagaDbSchemaChange`: a query that tells whether the change is applied, and the statements that apply it), and makes concurrent upgrades wait for each other in `RunExclusively`. Register it with `AddSagaDbSchemaInitialization<TUpgrade>()`: the database is created or upgraded when the host starts, or with the first context when there is no host.
- Instances that start together on a database that does not exist, or has no tables, each try to create it. Override the other `RunExclusively` of `SagaDbSchemaUpgrade`, the one that takes the creation of the tables, to make them wait for each other with a lock held by the connection (the tables are created in a transaction of their own): without it, all but one of them fail to start. Creating the database itself cannot be waited for: an instance that fails to create it goes on as soon as the database exists.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)