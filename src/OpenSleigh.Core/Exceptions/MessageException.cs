using System;

namespace OpenSleigh.Core.Exceptions
{
    public class MessageException : Exception
    {
        public MessageException(string message) : base(message){}
    }
}