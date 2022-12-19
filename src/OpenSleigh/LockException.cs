using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport
{
    [ExcludeFromCodeCoverage]
    public class LockException : Exception
    {
        public LockException(string msg) : base(msg)
        {
        }
    }
}