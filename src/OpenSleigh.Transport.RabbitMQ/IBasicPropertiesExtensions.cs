using RabbitMQ.Client;
using System.Text;

namespace OpenSleigh.Transport.RabbitMQ
{
    internal static class IBasicPropertiesExtensions
    {
        public static string GetHeaderValue(this IBasicProperties properties, string headerName)
        {
            if (!properties.Headers.TryGetValue(headerName, out var tmpVal))
                throw new ArgumentOutOfRangeException($"invalid header name: {headerName}");
            var bytes = tmpVal as byte[];
            if (bytes is null)
                throw new ArgumentException($"header '{headerName}' is invalid.");

            return Encoding.UTF8.GetString(bytes);
        }
    }
}