using RabbitMQ.Client;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>the text of a header, when the message has it and it is text.</summary>
    public static bool TryGetHeaderValue(this IReadOnlyBasicProperties properties, string headerName, [NotNullWhen(true)] out string? value)
    {
        value = properties.Headers is { } headers && headers.TryGetValue(headerName, out var header) && header is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;

        return value is not null;
    }
}
