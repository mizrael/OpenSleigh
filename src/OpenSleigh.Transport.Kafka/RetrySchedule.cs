using Confluent.Kafka;
using System.Globalization;
using System.Text;

namespace OpenSleigh.Transport.Kafka;

internal static class RetrySchedule
{
    public static Header CreateHeader(DateTimeOffset notBefore)
        => new(HeaderNames.NotBefore,
               Encoding.UTF8.GetBytes(notBefore.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));

    /// <returns>false when the message carries no readable not-before time: it can be processed straight away.</returns>
    public static bool TryGetNotBefore(Headers? headers, out DateTimeOffset notBefore)
    {
        notBefore = default;

        if (headers is null || !headers.TryGetLastBytes(HeaderNames.NotBefore, out var bytes) || bytes is null)
            return false;

        if (!long.TryParse(Encoding.UTF8.GetString(bytes), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds))
            return false;

        try
        {
            notBefore = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
