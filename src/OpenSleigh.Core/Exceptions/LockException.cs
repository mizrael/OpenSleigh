using System;

namespace OpenSleigh.Core.Exceptions
{
    public class LockException : Exception
    {
        public LockException(string msg) : base(msg)
        {
        }
    }
}