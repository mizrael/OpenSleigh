using System;

namespace OpenSleigh.Core.Exceptions
{
    public class SagaNotFoundException : Exception
    {
        public SagaNotFoundException(string message) : base(message)
        {
        }
    }
}