using System;

namespace OpenSleigh.Core.Exceptions
{
    public class ConsumerNotFoundException : Exception
    {
        public Type MessageType { get; }

        public ConsumerNotFoundException(Type messageType) : base($"no consumers found for message messageType '{messageType.FullName}'")
        {
            MessageType = messageType;
        }
    }
}