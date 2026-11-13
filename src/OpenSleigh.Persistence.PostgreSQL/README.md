# OpenSleigh.Persistence.PostgreSQL
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.PostgreSQL?style=plastic)

## Description
PostgreSQL Persistence library for OpenSleigh

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.PostgreSQL/

## How-to
The first thing to do is build an instance of `SqlConfiguration` with the connection details. This can be done by reading the current app configuration. 
Once done, all you have to do is to call the `UsePostgreSqlPersistence` extension method:
```

services.AddOpenSleigh(cfg =>{ 
    var connectionString = Configuration.GetConnectionString("sql");
    var sqlCfg = new SqlConfiguration(connectionString);
    cfg.UsePostgreSqlPersistence(sqlCfg);
    
    // register the Sagas here
});
```

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.InMemory/) or [RabbitMQ](https://www.nuget.org/packages/OpenSleigh.Transport.RabbitMQ/).

## Upgrading a database created by version 3.3.0 or earlier

Delayed messages need a column on the outbox table and an index on it. A new database is created with both. For a database that already exists, the library adds them by itself the first time the application uses the database after the upgrade:

```sql
ALTER TABLE opensleigh."OutboxMessages" ADD COLUMN IF NOT EXISTS "DeliverAt" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '1970-01-01T00:00:00+00:00';
CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_DeliverAt" ON opensleigh."OutboxMessages" ("DeliverAt");
```

- It first checks, with one query, whether the column and the index are there. When they are, nothing else runs: this is all that happens at every later start.
- The statements run in one transaction, and instances that start at the same time wait for each other (an advisory lock), so it is safe to start several at once. The statements do nothing when the column or the index already exists.
- Adding the column does not rewrite the table (PostgreSQL 11 and later). It takes a short exclusive lock on the outbox table, and waits for running transactions on it to end.
- **Permissions:** both statements require the database user to own the outbox table, which is the case when the application created the database. If the user cannot change the table, the application fails as soon as it uses the database, with an `InvalidOperationException` that lists the statements, and tries again the next time. Run the statements above with a user that can, before or after deploying the new version: once they are applied, the application needs no such permission.
- **Rolling upgrade:** instances of the earlier version keep working on the upgraded table, and the messages they store are published as before. They do not know about delays, though, and publish a delayed message as soon as they read it: do not publish messages with a delay until every instance runs the new version.
- **Going back:** the earlier version works on the upgraded table. To remove the change: `DROP INDEX opensleigh."IX_OutboxMessages_DeliverAt"; ALTER TABLE opensleigh."OutboxMessages" DROP COLUMN "DeliverAt";`

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)