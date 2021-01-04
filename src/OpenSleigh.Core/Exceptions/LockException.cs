using System;
using System.Runtime.Serialization;

namespace OpenSleigh.Core.Exceptions
{
    [Serializable]
    public class LockException : Exception
    {
        public LockException(string msg) : base(msg)
        {
        }

        protected LockException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}