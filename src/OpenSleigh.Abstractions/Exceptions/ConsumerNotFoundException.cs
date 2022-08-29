using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace OpenSleigh.Core.Exceptions
{
    [Serializable]
    [ExcludeFromCodeCoverage]
    public class ConsumerNotFoundException : Exception
    {
        public ConsumerNotFoundException(Type messageType) : base($"no consumers found for message messageType '{messageType.FullName}'")
        {
        }
        
        protected ConsumerNotFoundException(SerializationInfo info, StreamingContext context) 
            : base(info, context) { }
    }
}