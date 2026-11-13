# OpenSleigh.Persistence.SQL

# This package should not be used directly, unless you are intentionally using it to create a new SQL persistence layer. 

The following providers are already available:
- SQL Server: ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.SQLServer?style=plastic)
- PostgreSQL: ![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.PostgreSQL?style=plastic)

## Notes for a new provider

- Register the context with `services.AddPooledSagaDbContext(builder => ...)`. Every consumer still gets a `SagaDbContext` of its own, but contexts are reused once disposed instead of being built for every message.
- `SqlOutboxRepository` implements `IOutboxBatchRepository`, so the outbox relay deletes what it published in a pass with one round trip: one statement by default, which a provider can replace by overriding `DeleteBatchAsync` (SQL Server deletes by key). The relay reads, publishes and deletes inside one transaction, on the context of `SqlTransactionManager`: pass it to the repository as `relayDbContext`.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)