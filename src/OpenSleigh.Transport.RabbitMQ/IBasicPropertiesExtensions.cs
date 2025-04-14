using RabbitMQ.Client;
using System.Text;

namespace OpenSleigh.Transport.RabbitMQ;

internal static class IReadOnlyBasicPropertiesExtensions
{
    public static string GetHeaderValue(this IReadOnlyBasicProperties properties, string headerName)
    {
        if (true != properties.Headers?.TryGetValue(headerName, out var tmpVal))
            throw new ArgumentOutOfRangeException($"invalid header name: {headerName}");

        var bytes = tmpVal as byte[];
        if (bytes is null)
            throw new ArgumentException($"header '{headerName}' is invalid.");

        return Encoding.UTF8.GetString(bytes);
    }
}