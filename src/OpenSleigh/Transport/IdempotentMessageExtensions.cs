using System.Text;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport;

internal static class IdempotentMessageExtensions
{
    // TODO: tests
    public static string GetIdempotencyKey(this IIdempotentMessage message)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(message.RequestId))
            sb.Append(message.RequestId);

        var components = message.GetIdempotencyComponents();
        foreach (var component in components)
        {
            if (component is not null)
                sb.Append(component);
        }

        if (message is IHasCorrelationId hasCorrelationId &&
            !string.IsNullOrWhiteSpace(hasCorrelationId.CorrelationId))
        {
            sb.Append(hasCorrelationId.CorrelationId);
        }

        var key = sb.ToString();
        var hash = key.ComputeSha256Hash();
        return hash;
    }
}
