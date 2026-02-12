# OpenSleigh v3.1.0 🎉

## What's New

### 🚀 Multiple Saga Starters Support

Sagas can now be initiated by multiple different message types! This enables more flexible workflow orchestration patterns.

```csharp
public class OrderSaga : Saga<OrderState>,
    IStartedBy<OrderCreatedEvent>,      // Can start from order creation
    IStartedBy<PaymentReceivedEvent>,   // OR from payment received
    IHandleMessage<InventoryReservedEvent>
{
    // Either message can initiate the saga
    // Optimistic locking prevents duplicates
}
```

Thanks to @arootbeer for the implementation! (#101)

---

## 🐛 Critical Bug Fixes

### SqlSagaStateRepository Lock Query Filtering
**Fixed:** Lock queries were missing saga type filters, causing wrong saga retrieval when multiple saga types shared correlation IDs.
- **Impact:** HIGH - Could cause state corruption in multi-saga workflows
- **Fixed in:** `SqlSagaStateRepository.cs`

### SagaExecutionService Exception Masking
**Fixed:** Retry failures now preserve both original and retry exceptions in an `AggregateException`.
- **Impact:** HIGH - Production debugging was nearly impossible
- **Fixed in:** `SagaExecutionService.cs`

### RabbitMQ Duplicate Event Handler
**Fixed:** Removed duplicate `ConnectionShutdownAsync` registration causing race conditions.
- **Impact:** MEDIUM - Unnecessary reconnection attempts
- **Fixed in:** `RabbitPersistentConnection.cs`

### SQL Outbox Semaphore Bottleneck
**Fixed:** Changed static semaphore to instance-level, eliminating global serialization.
- **Impact:** MEDIUM - Severe performance bottleneck removed
- **Fixed in:** `SqlOutboxRepository.cs`

### RabbitMQ Cancellation Token
**Fixed:** Proper cancellation token propagation enables graceful shutdown.
- **Impact:** MEDIUM - Apps could hang during shutdown
- **Fixed in:** `RabbitMessageSubscriber.cs`

---

## ✅ Testing

- **8 new unit tests** for repository and execution service
- **1 new integration test** for multi-saga correlation scenarios
- **116 total unit tests passing**
- **Zero regressions**

---

## 📦 Package Updates

All OpenSleigh packages updated to **v3.1.0**:
- OpenSleigh
- OpenSleigh.InMemory
- OpenSleigh.Persistence.SQL / SQLServer / PostgreSQL / Mongo
- OpenSleigh.Transport.RabbitMQ / Kafka

---

## 🔄 Breaking Changes

**None for standard usage!** Fully backward compatible.

⚠️ Internal API changes only affect extensions of:
- `SagaDescriptor.InitiatorType` → `InitiatorTypes` (now `ISet<Type>`)
- `TypeExtensions.GetInitiatorMessageType()` returns `ISet<Type>`

---

## 📚 Documentation

- [Multiple Saga Starters Guide](https://opensleigh.gitbook.io/guides/multiple-starters)
- [Optimistic Concurrency Control](https://opensleigh.gitbook.io/guides/concurrency)
- [Migration Guide](https://opensleigh.gitbook.io/)

---

## 🙏 Contributors

- @arootbeer - Multiple saga starters feature
- @mizrael - Bug fixes, testing, and release management

---

## 📋 Upgrade Instructions

```bash
# Update packages
dotnet add package OpenSleigh --version 3.1.0
dotnet add package OpenSleigh.Persistence.SQL --version 3.1.0
dotnet add package OpenSleigh.Transport.RabbitMQ --version 3.1.0
```

No code changes required. Run your test suite and enjoy! 🚀

---

**Full Changelog:** [CHANGELOG-3.1.0.md](./CHANGELOG-3.1.0.md)
**Compare:** [`v3.0.6...v3.1.0`](https://github.com/mizrael/OpenSleigh/compare/v3.0.6...v3.1.0)
