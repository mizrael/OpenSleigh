using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace OpenSleigh.Core.Exceptions
{
    [Serializable]
    [ExcludeFromCodeCoverage]
    public class MessageException : Exception
    {
        public MessageException(string message) : base(message){}

        protected MessageException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}