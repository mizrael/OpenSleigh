using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Messaging
{
    [ExcludeFromCodeCoverage]
    public class SagaException : Exception
    {
        public SagaException(string msg, Exception innerException) : base(msg, innerException)
        {
        }
    }
}