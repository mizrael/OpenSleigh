namespace OpenSleigh.Persistence.SQL;

public enum TableHints
{
    Index,
    KeepIdentity,
    KeepDefaults,
    HoldLock,
    Ignore_Constraints,
    Ignore_Triggers,
    Nolock,
    NoWait,
    PagLock,
    ReadCommitted,
    ReadCommittedLock,
    ReadPast,
    RepeatableRead,
    RowLock,
    Serializable,
    Snapshot,
    TabLock,
    TabLockX,
    UpdLock,
    Xlock
}