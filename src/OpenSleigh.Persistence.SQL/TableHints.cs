namespace OpenSleigh.Persistence.SQL;

[Obsolete("No longer used by OpenSleigh, which states its table hints in the statement that reads the outbox. It will be removed in a future version.")]
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