# OpenSleigh v3.1.0 Release Notes

**Release Date:** January 10, 2026

This release includes the highly-requested multiple saga starters feature from PR #101, along with critical bug fixes and performance improvements identified during comprehensive code review.

---

## 🚀 New Features

### Multiple Saga Starters Support (#101)

Sagas can now be initiated by multiple different message types, enabling more flexible workflow orchestration patterns.

**What's New:**
- Sagas can implement multiple `IStartedBy<TMessage>` interfaces
- Each starter message can independently trigger saga creation
- Optimistic concurrency control ensures only one saga instance is created when multiple starters arrive concurrently
- Full E2E test coverage for sequential and parallel multi-start scenarios

**Example:**
```csharp
public class OrderSaga : Saga<OrderState>,
    IStartedBy<OrderCreatedEvent>,      // Can start from order creation
    IStartedBy<PaymentReceivedEvent>,   // OR from payment received
    IHandleMessage<InventoryReservedEvent>
{
    // Saga implementation
}
```

**Architecture Changes:**
- `SagaDescriptor.InitiatorType` → `SagaDescriptor.InitiatorTypes` (now a set)
- `TypeExtensions.GetInitiatorMessageType()` returns `ISet<Type>` instead of single type
- New `OptimisticLockException` for handling concurrent saga creation attempts

**Contributors:** @arootbeer, @mizrael

---

## 🐛 Critical Bug Fixes

### Lock Query Filtering (SqlSagaStateRepository)

**Issue:** Lock queries in SQL-based repositories were missing saga type and state type filters, causing incorrect saga instance retrieval in multi-saga scenarios where different saga types share the same correlation ID.

**Impact:** HIGH - Could retrieve wrong saga instance, causing state corruption in distributed workflows.

**Fixed:**
- Added saga type filtering to `LockAsyncCore()` initial query (line 96-101)
- Added saga type filtering to optimistic lock retry query (line 141-146)
- Ensures queries match `FindAsync()` filtering behavior for consistency

**Files Modified:**
- `src/OpenSleigh.Persistence.SQL/SqlSagaStateRepository.cs`

**Test Coverage:**
- New integration test: `LockAsync_should_handle_multiple_saga_types_with_same_correlation_id()`

---

### Exception Masking in Retry Logic (SagaExecutionService)

**Issue:** When optimistic lock conflicts occurred and retry failed for a different reason (e.g., database connection failure, timeout), the catch-all block swallowed the retry exception and re-threw only the original `OptimisticLockException`, masking the real problem.

**Impact:** HIGH - Made production debugging extremely difficult by hiding actual failure causes (network issues, database outages, etc.)

**Fixed:**
- Refactored retry logic into `BeginProcessingCore()` for cleaner separation
- Retry failures now throw `AggregateException` containing both:
  - Original `OptimisticLockException`
  - Retry failure exception with full stack trace
- Descriptive error message: "Failed to lock saga after optimistic lock conflict"

**Files Modified:**
- `src/OpenSleigh/SagaExecutionService.cs`

**Test Coverage:**
- `BeginProcessingAsync_should_wrap_retry_exception_when_retry_fails_for_different_reason()`
- `BeginProcessingAsync_should_succeed_on_retry_when_lock_acquired()`
- `CommitAsync_should_append_outbox_and_release_lock()`

---

### Duplicate Event Handler Registration (RabbitPersistentConnection)

**Issue:** `ConnectionShutdownAsync` event handler was registered twice, causing reconnection logic to execute concurrently on connection loss.

**Impact:** MEDIUM - Race conditions in semaphore-protected reconnection logic, unnecessary resource consumption.

**Fixed:**
- Removed duplicate event handler registration (line 66)
- Connection shutdown now triggers exactly one reconnection attempt

**Files Modified:**
- `src/OpenSleigh.Transport.RabbitMQ/RabbitPersistentConnection.cs`

---

### Static Semaphore Bottleneck (SqlOutboxRepository)

**Issue:** The `_appendSemaphore` was declared as `static`, causing all `SqlOutboxRepository` instances across the application to share a single semaphore.

**Impact:** MEDIUM - Severe performance bottleneck in multi-tenant or multi-context scenarios; all outbox operations globally serialized.

**Fixed:**
- Changed semaphore from `static` to instance-level field
- Each repository now has independent concurrency control
- Significant performance improvement under high load

**Files Modified:**
- `src/OpenSleigh.Persistence.SQL/SqlOutboxRepository.cs`

---

### Missing Null Check in Configuration (RabbitMessageSubscriber)

**Issue:** `_rabbitConfiguration` parameter was not validated for null, inconsistent with other constructor parameters.

**Impact:** LOW - Unclear error messages if configuration injection failed.

**Fixed:**
- Added `ArgumentNullException.ThrowIfNull()` validation
- Consistent with other dependency validation patterns

**Files Modified:**
- `src/OpenSleigh.Transport.RabbitMQ/RabbitMessageSubscriber.cs`

---

### Cancellation Token Propagation (RabbitMessageSubscriber)

**Issue:** Message processing always used `CancellationToken.None` instead of propagating the cancellation token, preventing graceful shutdown of long-running handlers.

**Impact:** MEDIUM - Application could hang indefinitely during shutdown waiting for message handlers to complete.

**Fixed:**
- Cancellation token now properly propagated to `IMessageProcessor.ProcessAsync()`
- Long-running handlers can be interrupted during graceful shutdown

**Files Modified:**
- `src/OpenSleigh.Transport.RabbitMQ/RabbitMessageSubscriber.cs`

---

## 🧪 Testing Improvements

### New Test Coverage

**Unit Tests:**
- **InMemorySagaStateRepositoryTests.cs** (4 tests)
  - Lock acquisition and release
  - Concurrent lock handling with OptimisticLockException
  - FindAsync by correlation ID
  - Same instance re-locking behavior

- **SagaExecutionServiceTests.cs** (3 tests)
  - Exception wrapping in retry failures
  - Successful retry after optimistic lock conflict
  - Commit with outbox and lock release

**Integration Tests:**
- **SqlSagaStateRepositoryTests** (1 test)
  - Multiple saga types with shared correlation ID

**E2E Tests (from PR #101):**
- Sequential multi-start saga scenarios
- Parallel multi-start saga scenarios with race condition handling
- InMemory and SQL+RabbitMQ transport combinations

**Total New Tests:** 8 unit tests + extensive E2E coverage

**Test Results:**
- ✅ 116 unit tests passing (62 core + 31 Kafka + 23 RabbitMQ)
- ✅ All integration tests passing (with Docker infrastructure)
- ✅ Zero regressions

---

## 📦 Package Updates

**Version:** 3.0.6 → **3.1.0**

All OpenSleigh NuGet packages updated:
- `OpenSleigh`
- `OpenSleigh.InMemory`
- `OpenSleigh.Persistence.SQL`
- `OpenSleigh.Persistence.SQLServer`
- `OpenSleigh.Persistence.PostgreSQL`
- `OpenSleigh.Persistence.Mongo`
- `OpenSleigh.Transport.RabbitMQ`
- `OpenSleigh.Transport.Kafka`

---

## 🔄 Breaking Changes

### None for Standard Usage

The API changes are **backward compatible** for typical usage:
- Single starter sagas work exactly as before
- `SagaDescriptor.InitiatorTypes` is a set containing the single type
- No changes required to existing saga implementations

### Internal API Changes

⚠️ **If you're extending OpenSleigh internals:**

1. **SagaDescriptor.InitiatorType → InitiatorTypes**
   - Now returns `ISet<Type>` instead of `Type`
   - Use `.Contains()` instead of direct equality check

2. **TypeExtensions.GetInitiatorMessageType()**
   - Returns `ISet<Type>` instead of single `Type`
   - Callers must handle multiple types

---

## 📝 Migration Guide

### From 3.0.x to 3.1.0

**No code changes required** for standard saga usage. Simply update your package references:

```xml
<PackageReference Include="OpenSleigh" Version="3.1.0" />
<PackageReference Include="OpenSleigh.Persistence.SQL" Version="3.1.0" />
<PackageReference Include="OpenSleigh.Transport.RabbitMQ" Version="3.1.0" />
```

**To use multiple saga starters:**

```csharp
// Before (single starter)
public class MySaga : Saga<MyState>,
    IStartedBy<StartMessage>
{
    // ...
}

// After (multiple starters)
public class MySaga : Saga<MyState>,
    IStartedBy<StartMessageA>,
    IStartedBy<StartMessageB>
{
    // Both messages can now initiate the saga
    // First message to arrive creates the instance
    // Optimistic locking prevents duplicates
}
```

---

## ⚡ Performance Improvements

1. **SqlOutboxRepository:** Removed static semaphore bottleneck - eliminates global serialization of outbox appends
2. **RabbitMQ Connection:** Eliminated duplicate reconnection attempts on connection loss
3. **Cancellation Support:** Faster graceful shutdown with proper cancellation token propagation

---

## 🙏 Acknowledgments

Special thanks to:
- **@arootbeer** for implementing the multiple saga starters feature
- All contributors who tested the preview releases
- The community for reporting and discussing issues

---

## 📚 Documentation

Updated documentation available at: https://opensleigh.gitbook.io/

New guides:
- [Multiple Saga Starters](https://opensleigh.gitbook.io/guides/multiple-starters)
- [Optimistic Concurrency Control](https://opensleigh.gitbook.io/guides/concurrency)
- [Graceful Shutdown Best Practices](https://opensleigh.gitbook.io/guides/shutdown)

---

## 🔗 Full Changelog

**Commits:**
- `2922441` - Updated package version to 3.1.0
- `3aaacdc` - Minor fixes (RabbitMQ, SQL, cancellation token)
- `6b71ee7` - Multiple fixes (lock filtering, exception handling, tests)
- `10b1903` - Merged PR #101 (multiple saga starters)
- `96fb93c` - Cleanup
- `893ac0b` - E2E tests for optimistic concurrency
- `115390f` - E2E tests for in-memory transport and persistence
- `3ed2f9a` - Core implementation: multiple message types can start sagas

**Compare:** https://github.com/mizrael/OpenSleigh/compare/v3.0.6...v3.1.0

---

## 🐛 Known Issues

None at this time. Report issues at: https://github.com/mizrael/OpenSleigh/issues

---

## 📋 Upgrade Checklist

- [ ] Update all OpenSleigh package references to 3.1.0
- [ ] Run full test suite to ensure compatibility
- [ ] Review changes if you extend `SagaDescriptor` or `TypeExtensions`
- [ ] Consider implementing multiple starters if your workflows require it
- [ ] Test graceful shutdown behavior with cancellation tokens
- [ ] Monitor performance improvements in SQL outbox operations

---

**Questions or Issues?**
- GitHub Issues: https://github.com/mizrael/OpenSleigh/issues
- Discussions: https://github.com/mizrael/OpenSleigh/discussions
- Documentation: https://opensleigh.gitbook.io/

Happy saga orchestrating! 🎉
