using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh;

[ExcludeFromCodeCoverage]
public class LockException : Exception
{
    public LockException(string msg) : base(msg)
    {
    }
}