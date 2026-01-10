using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh;

[ExcludeFromCodeCoverage]
public class OptimisticLockException : Exception
{
    public OptimisticLockException(string message = "Unable to optimistically acquire lock") : base(message) { }
}