# OpenSleigh.Persistence.SQLServer
![Nuget](https://img.shields.io/nuget/v/OpenSleigh.Persistence.SQLServer?style=plastic)

## Description
SQL Server Persistence library for OpenSleigh.

For more details, check the [Official Documentation](https://opensleigh.gitbook.io/).

## Installation
This library can be installed from Nuget: https://www.nuget.org/packages/OpenSleigh.Persistence.SQLServer/

## How-to
The first thing to do is build an instance of `SqlConfiguration` with the connection details. This can be done by reading the current app configuration. Once done, all you have to do is to call the `UseSqlServerPersistence` extension method:
```

services.AddOpenSleigh(cfg =>{ 
    var connectionString = Configuration.GetConnectionString("sql");
    var sqlCfg = new SqlConfiguration(connectionString);
    cfg.UseSqlServerPersistence(sqlCfg);
    
    // register the Sagas here
});
```

Of course it can be used in conjunction with any other Transport library as well (eg. [InMemory](https://www.nuget.org/packages/OpenSleigh.InMemory/) or [RabbitMQ](https://www.nuget.org/packages/OpenSleigh.Transport.RabbitMQ/).

## The first start

The database and its tables are created when the application starts, if they are missing. Several instances can start together on a server that does not have the database yet: the ones that find it created by another go on, and they wait for each other (an application lock) to create the tables, which only the first one does. When the tables are there, a start costs the same two checks as before, and takes no lock.

## Upgrading a database created by version 3.3.0 or earlier

Delayed messages need a column on the outbox table and an index on it. A new database is created with both. A database that already exists gets them from the library when the application starts (or, without a host, the first time it uses the database):

```sql
IF COL_LENGTH(N'[opensleigh].[OutboxMessages]', N'DeliverAt') IS NULL
    ALTER TABLE [opensleigh].[OutboxMessages] ADD [DeliverAt] datetimeoffset NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[opensleigh].[OutboxMessages]') AND name = N'IX_OutboxMessages_DeliverAt')
    CREATE INDEX [IX_OutboxMessages_DeliverAt] ON [opensleigh].[OutboxMessages] ([DeliverAt]); -- WITH (ONLINE = ON) where the edition can
```

- It first checks, with one query each, whether the column and the index are there. When they are, nothing else runs: this is all that happens at every later start.
- Instances that start at the same time wait for each other (an application lock), and each checks again once it has the lock: only the first one changes anything, so it is safe to start several at once.
- The column is nullable and has no default, so adding it does not rewrite the table, on any edition and whatever its size. It needs the table for a moment only, and gives up after 5 seconds if it cannot have it: a long transaction on the outbox table makes the upgrade fail instead of making everything else wait behind it.
- The index is built in a second step, with 10 minutes to do it. On the Enterprise and Developer editions, Azure SQL Database and Azure SQL Managed Instance it is built online: the outbox is read and written as usual meanwhile. **On the other editions (Standard, Web, Express) nothing can be stored in the outbox, or relayed from it, while the index is built**: that is every instance, including the ones still on the earlier version.
- **If it fails**, the application does not start: the error says why, and lists the statements. Nothing is half done: either step can be applied again. The cause is logged, as are the start and the end of each step.
- **Permissions:** both statements require the `ALTER` permission on the outbox table (members of `db_owner` and `db_ddladmin` have it), which is the case when the application created the database. Otherwise, run the statements above with a user that has it, before or after deploying the new version: once they are applied, the application needs no such permission.
- **A large outbox** (millions of rows) on an edition that cannot build the index online: apply the statements by hand before deploying, at a time when the outbox can wait. The earlier version works on the upgraded table.
- **Rolling upgrade:** instances of the earlier version keep working on the upgraded table, and the messages they store are published as before. They do not know about delays, though: **an instance of the earlier version publishes a delayed message as soon as it reads it.** Do not publish messages with a delay until every instance runs the new version.
- **Going back** to the earlier version: for the same reason, every delayed message still in the outbox is published at once by the earlier version. Before going back, wait until none is left, or remove them (they are lost):

  ```sql
  SELECT COUNT(*) FROM [opensleigh].[OutboxMessages] WHERE [DeliverAt] > SYSDATETIMEOFFSET();
  DELETE FROM [opensleigh].[OutboxMessages] WHERE [DeliverAt] > SYSDATETIMEOFFSET();
  ```

  The column and the index can stay: the earlier version ignores them. Dropping the column erases the due times of the messages that are waiting.
- The outbox is read through the `IX_OutboxMessages_DeliverAt` index, by name: do not rename or drop it.
- `QueryHintInterceptor`, `WithHint` and `TableHints` are no longer used and are marked obsolete: the interceptor is not registered any more. An application that used `WithHint` on its own queries of the saga context has to register the interceptor itself.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)
