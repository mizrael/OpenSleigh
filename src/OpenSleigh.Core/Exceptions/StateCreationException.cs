using System;

namespace OpenSleigh.Core.Exceptions
{
    public class StateCreationException : Exception
    {
        public StateCreationException(Type stateType, Guid correlationId) 
            : base($"unable to create State instance with type '{stateType.FullName}' for Saga '{correlationId}'")
        {
        }
    }
}