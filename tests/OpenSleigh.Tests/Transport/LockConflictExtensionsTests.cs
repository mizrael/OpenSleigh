using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Transport;

public class LockConflictExtensionsTests
{
    public static TheoryData<Exception, bool> Failures => new()
    {
        { new LockException("locked by someone else"), true },
        { new OptimisticLockException(), true },
        { new AggregateException(new OptimisticLockException(), new InvalidOperationException("whoops")), true },
        { new AggregateException(new InvalidOperationException("whoops"), new LockException("locked by someone else")), true },
        { new AggregateException(new InvalidOperationException("whoops")), false },
        { new InvalidOperationException("whoops"), false },
        { new SagaException("the handler failed", new LockException("thrown by the handler itself")), false },
        { new OperationCanceledException(), false }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void IsLockConflict_should_tell_lock_conflicts_from_other_failures(Exception failure, bool expected)
    {
        Assert.Equal(expected, failure.IsLockConflict());
    }
}
