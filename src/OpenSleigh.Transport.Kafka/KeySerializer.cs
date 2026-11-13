using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

internal class KeySerializer<TKey> : ISerializer<TKey>
{
    public byte[] Serialize(TKey data, SerializationContext context)
    {
        if (data is Guid g)
            return g.ToByteArray();

        // a consumer reads the key back as text, quotes included, and that is the id of the message it processes.
        // A message that is published again, to a retry or to a dead letter topic, keeps that id as it is:
        // quoted once more it would be another message to whoever receives it
        if (data is string text && IsJsonString(text))
            return Encoding.UTF8.GetBytes(text);

        var json = JsonSerializer.Serialize(data);
        return Encoding.UTF8.GetBytes(json);
    }

    private static bool IsJsonString(string text)
        => text.Length >= 2 && text[0] == Quote && text[^1] == Quote;

    private const char Quote = '"';
}