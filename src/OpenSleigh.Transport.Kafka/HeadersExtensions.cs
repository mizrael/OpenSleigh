using Confluent.Kafka;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace OpenSleigh.Transport.Kafka;

internal static class HeadersExtensions
{
    public static string GetHeaderValue(this Headers headers, string headerName)
    {
        if (!headers.TryGetLastBytes(headerName, out var bytes))
            throw new ArgumentException($"invalid header name: {headerName}");

        if (bytes is null)
            throw new ArgumentException($"content of header '{headerName}' is invalid.");

        return Encoding.UTF8.GetString(bytes);
    }

    public static bool TryGetHeaderValue(this Headers headers, string headerName, [NotNullWhen(true)] out string? value)
    {
        if (headers.TryGetLastBytes(headerName, out var bytes) && bytes is not null)
        {
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        value = null;
        return false;
    }
}
