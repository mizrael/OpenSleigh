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

## Upgrading a database created by version 3.3.0 or earlier

Delayed messages need a column on the outbox table and an index on it. A new database is created with both. For a database that already exists, the library adds them by itself the first time the application uses the database after the upgrade:

```sql
IF COL_LENGTH(N'[opensleigh].[OutboxMessages]', N'DeliverAt') IS NULL
    ALTER TABLE [opensleigh].[OutboxMessages] ADD [DeliverAt] datetimeoffset NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[opensleigh].[OutboxMessages]') AND name = N'IX_OutboxMessages_DeliverAt')
    CREATE INDEX [IX_OutboxMessages_DeliverAt] ON [opensleigh].[OutboxMessages] ([DeliverAt]);
```

- It first checks, with one query, whether the column and the index are there. When they are, nothing else runs: this is all that happens at every later start.
- The statements run in one transaction, and instances that start at the same time wait for each other (an application lock), so it is safe to start several at once. The statements do nothing when the column or the index already exists.
- Both statements need a schema modification lock on the outbox table, and wait for running transactions on it to end. Depending on the edition of SQL Server, adding the column may rewrite the rows of the table: the outbox is normally almost empty.
- **Permissions:** both statements require the `ALTER` permission on the outbox table (members of `db_owner` and `db_ddladmin` have it), which is the case when the application created the database. If the user cannot change the table, the application fails as soon as it uses the database, with an `InvalidOperationException` that lists the statements, and tries again the next time. Run the statements above with a user that can, before or after deploying the new version: once they are applied, the application needs no such permission.
- **Rolling upgrade:** instances of the earlier version keep working on the upgraded table, and the messages they store are published as before. They do not know about delays, though, and publish a delayed message as soon as they read it: do not publish messages with a delay until every instance runs the new version.
- **Going back:** the earlier version works on the upgraded table. To remove the change, drop the index, the default constraint of the column, and the column.
- The outbox is read through the `IX_OutboxMessages_DeliverAt` index, by name: do not rename or drop it.

---

Do you like this project? Then consider giving a donation! [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=9F94U4GWN7YS6&currency_code=CAD&item_name=OpenSleigh)
