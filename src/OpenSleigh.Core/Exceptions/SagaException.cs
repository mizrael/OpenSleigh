using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace OpenSleigh.Core.Exceptions
{
    [Serializable]
    [ExcludeFromCodeCoverage]
    public class SagaException : Exception
    {
        public SagaException(string message) : base(message) { }

        protected SagaException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}