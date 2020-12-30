using System;

namespace OpenSleigh.Core.Exceptions
{
    public class StateCreationException : Exception
    {
        public Type StateType { get; }

        public StateCreationException(Type stateType, string message) : base(message)
        {
            StateType = stateType;
        }
    }
}