using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh;

[ExcludeFromCodeCoverage]
public class SagaException : Exception
{
    public SagaException(string msg, Exception innerException) : base(msg, innerException)
    {
    }

    /// <summary>
    /// true when the handler of the message failed and its rollback ran to the end: what the rollback published
    /// is stored with the saga, and the message is recorded as processed. False when the rollback failed as well.
    /// </summary>
    public bool RolledBack { get; init; }
}