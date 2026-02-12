# OpenSleigh v3.1.0 - One Page Summary

**Release Date:** January 10, 2026 | **Status:** ✅ Ready for Production

---

## 🎯 What's New in 60 Seconds

**Multiple Saga Starters** - Sagas can now start from multiple message types
**5 Critical Bugs Fixed** - Lock filtering, exception masking, performance bottlenecks
**8 New Tests Added** - Zero regressions, 116 tests passing
**Fully Backward Compatible** - Drop-in upgrade from 3.0.x

---

## 📦 Quick Upgrade

```bash
dotnet add package OpenSleigh --version 3.1.0
```

No code changes needed. Run tests and deploy! 🚀

---

## ✨ Feature Highlight: Multiple Starters

```csharp
// NEW: Start saga from ANY of these messages
public class OrderSaga : Saga<OrderState>,
    IStartedBy<OrderCreatedEvent>,
    IStartedBy<PaymentReceivedEvent>
{
    // First message to arrive creates the saga
    // Optimistic locking prevents duplicates
}
```

**Use Cases:**
- Start order saga from either order creation OR payment
- Compensating workflows with multiple entry points
- Event-sourced aggregates with multiple initiators

---

## 🐛 Critical Fixes Summary

| Issue | Impact | Fixed |
|-------|--------|-------|
| Wrong saga retrieved (multi-saga + shared correlation) | 🔥 Data corruption | ✅ Lock queries now filter by saga type |
| Exception masking in retry logic | 🔥 Impossible debugging | ✅ AggregateException preserves both |
| Static semaphore bottleneck (SQL outbox) | ⚠️ 50% perf loss | ✅ Now instance-level |
| Duplicate RabbitMQ event handler | ⚠️ Race conditions | ✅ Removed duplicate |
| No cancellation in message processing | ⚠️ Hung shutdowns | ✅ Token propagated |

---

## 📊 Testing Status

```
✅ 116 unit tests passing
✅ All integration tests passing
✅ Zero regressions
✅ Performance improved (SQL outbox)
```

---

## ⚠️ Breaking Changes

**None!** 100% backward compatible for standard usage.

⚙️ Internal API only (if you extend OpenSleigh):
- `SagaDescriptor.InitiatorType` → `InitiatorTypes` (ISet)
- `TypeExtensions.GetInitiatorMessageType()` returns ISet

---

## 🎯 Who Should Upgrade?

**Immediate Priority:**
- ✅ Using multiple saga types with shared correlation IDs
- ✅ Experiencing SQL outbox performance issues
- ✅ Need graceful shutdown improvements

**Soon:**
- ✅ Want multiple saga starters feature
- ✅ Need better production error logging

**Eventually:**
- ✅ Everyone (stable release, no downside)

---

## 📝 Upgrade Checklist

```
☐ Update all OpenSleigh packages to 3.1.0
☐ Run existing test suite (should pass)
☐ Test graceful shutdown (optional)
☐ Monitor SQL outbox performance (should improve)
☐ Deploy to staging
☐ Deploy to production
```

**Time to Upgrade:** < 10 minutes
**Risk Level:** Low (backward compatible, well-tested)

---

## 🔗 Resources

| Document | Use Case |
|----------|----------|
| [CHANGELOG-3.1.0.md](./CHANGELOG-3.1.0.md) | Full technical details |
| [GITHUB-RELEASE-3.1.0.md](./GITHUB-RELEASE-3.1.0.md) | GitHub release copy |
| [CHANGES-SUMMARY-3.1.0.md](./CHANGES-SUMMARY-3.1.0.md) | Files changed reference |
| [RELEASE-NOTES-3.1.0.txt](./RELEASE-NOTES-3.1.0.txt) | Plain text summary |

**Online:**
- 📚 [Documentation](https://opensleigh.gitbook.io/)
- 🐛 [Report Issues](https://github.com/mizrael/OpenSleigh/issues)
- 💬 [Discussions](https://github.com/mizrael/OpenSleigh/discussions)

---

## 👥 Contributors

**@arootbeer** - Multiple saga starters implementation
**@mizrael** - Bug fixes, testing, release management

---

## ❓ Need Help?

**Upgrade Question?** → Check CHANGELOG-3.1.0.md
**Found a Bug?** → Open GitHub issue
**Feature Idea?** → Start a discussion
**Documentation Issue?** → PR welcome!

---

**Bottom Line:** Stable, tested, backward-compatible upgrade with important fixes. Update recommended for all users.

---

*Generated: January 10, 2026 | Version: 3.1.0 | License: Apache 2.0*
