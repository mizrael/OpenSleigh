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

## The first start

The database and its tables are created when the application starts, if they are missing. Several instances can start together on a server that does not have the database yet: they wait for each other to create it (an advisory lock in the `postgres` database), and then to create the tables (an advisory lock in the database), which only the first one does. When the tables are there, a start costs the same two checks as before, and takes no lock.

`UsePostgreSqlPersistence` configures the context this way. A context registered otherwise (`AddSagaDbSchemaInitialization`) differs in what follows:

- **`UseSeeding`:** every start takes the advisory lock in the database to seed, one instance at a time, and can wait up to 10 minutes for another instance.
- **`UseAdminDatabase`:** the lock for the creation is taken there. It must not be the template the database is copied from (`template1` unless the model names another): PostgreSQL does not copy a database that is in use, and the application fails to start on a missing database, saying so.
- **Time to create the database:** `CREATE DATABASE` runs with the command timeout of the context (30 seconds unless the connection string has `Command Timeout`), not with the 10 minutes of the other statements.

The lock for the creation is taken through the connection the EF Core provider creates the database with, which is not part of its public API: it was run against Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 with a server, and 9.0.4 up to opening the connection.

## Upgrading a database created by version 3.3.0 or earlier

Delayed messages need a column on the outbox table and an index on it. A new database is created with both. A database that already exists gets them from the library when the application starts (or, without a host, the first time it uses the database):

```sql
ALTER TABLE opensleigh."OutboxMessages" ADD COLUMN IF NOT EXISTS "DeliverAt" timestamp with time zone;
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_OutboxMessages_DeliverAt" ON opensleigh."OutboxMessages" ("DeliverAt");
-- when a transport first returned a message that nothing could take (RabbitMQ)
ALTER TABLE opensleigh."OutboxMessages" ADD COLUMN IF NOT EXISTS "FirstReturnedAt" timestamp with time zone;
```

- It first checks, with one query each, whether the column and the index are there. When they are, nothing else runs: this is all that happens at every later start.
- Instances that start at the same time wait for each other (an advisory lock), and each checks again once it has the lock: only the first one changes anything, so it is safe to start several at once.
- The column has no default, so adding it does not rewrite the table, whatever its size. It needs the table for a moment only, and gives up after 5 seconds if it cannot have it: a long transaction on the outbox table makes the upgrade fail instead of making everything else wait behind it.
- The index is built with `CONCURRENTLY`, in a second step: the outbox is read and written as usual while it is built. PostgreSQL waits for the transactions that were running to end before it finishes, and the statement has 10 minutes. A build that fails leaves an index that is not valid: the library drops it and builds it again at the next start.
- **If it fails**, the application does not start: the error says why, and lists the statements. Nothing is half done: either step can be applied again. The cause is logged, as are the start and the end of each step.
- **Permissions:** both statements require the database user to own the outbox table, which is the case when the application created the database. Otherwise, run the statements above with a user that does, before or after deploying the new version: once they are applied, the application needs no such permission. Putting a message a RabbitMQ broker returned back in the outbox needs the `UPDATE` privilege on the outbox table, besides the ones to insert, read and delete.
- **A large outbox** (millions of rows): apply the statements by hand before deploying, at a time of your choosing. The earlier version works on the upgraded table.
- **Rolling upgrade:** instances of the earlier version keep working on the upgraded table, and the messages they store are published as before. They do not know about delays, though: **an instance of the earlier version publishes a delayed message as soon as it reads it.** Do not publish messages with a delay until every instance runs the new version. The same goes for the messages a RabbitMQ broker returns, which the new version puts back in the outbox with a due time and the time they first came back: an instance of the earlier version publishes such a message at once, and without that time, so its retry period starts again. Set `ReturnedMessageRetryPeriod` to zero on the new instances until every instance runs the new version, so that returned messages are not put back.
- **Going back** to the earlier version: for the same reason, every delayed message still in the outbox is published at once by the earlier version. Before going back, wait until none is left, or remove them (they are lost; the returned messages that are waiting to be published again are among them):

  ```sql
  SELECT COUNT(*) FROM opensleigh."OutboxMessages" WHERE "DeliverAt" > now();
  DELETE FROM opensleigh."OutboxMessages" WHERE "DeliverAt" > now();
  ```

  The column and the index can stay: the earlier version ignores them. Dropping the column erases the due times of the messages that are waiting.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)