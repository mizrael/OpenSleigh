using Confluent.Kafka;
using System.Text;

namespace OpenSleigh.Transport.Kafka
{
    internal static class HeadersExtensions
    {
        public static string GetHeaderValue(this Headers headers, string headerName)
        {
            if (!headers.TryGetLastBytes(headerName, out var bytes))
                throw new ArgumentOutOfRangeException($"invalid header name: {headerName}");

            if (bytes is null)
                throw new ArgumentException($"header '{headerName}' is invalid.");

            return Encoding.UTF8.GetString(bytes);
        }
    }
}
