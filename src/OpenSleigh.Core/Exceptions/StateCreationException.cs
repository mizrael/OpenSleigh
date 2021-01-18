using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace OpenSleigh.Core.Exceptions
{
    [Serializable]
    [ExcludeFromCodeCoverage]
    public class StateCreationException : Exception
    {
        public StateCreationException(string message) : base(message) { }
        
        public StateCreationException(Type stateType, Guid correlationId) 
            : base($"unable to create State instance with type '{stateType.FullName}' for Saga '{correlationId}'")
        {
        }

        protected StateCreationException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}