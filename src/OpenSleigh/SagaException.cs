using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport
{
    [ExcludeFromCodeCoverage]
    public class SagaException : Exception
    {
        public SagaException(string msg, Exception innerException) : base(msg, innerException)
        {
        }
    }
}