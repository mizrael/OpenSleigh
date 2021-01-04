using System;
using System.Runtime.Serialization;

namespace OpenSleigh.Core.Exceptions
{
    [Serializable]
    public class StateCreationException : Exception
    {
        public StateCreationException(Type stateType, Guid correlationId) 
            : base($"unable to create State instance with type '{stateType.FullName}' for Saga '{correlationId}'")
        {
        }

        protected StateCreationException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}