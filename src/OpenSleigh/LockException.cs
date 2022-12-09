using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Messaging
{
    [ExcludeFromCodeCoverage]
    public class LockException : Exception
    {
        public LockException(string msg) : base(msg)
        {
        }
    }
}