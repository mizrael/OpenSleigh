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

        // a string is written as it is: as JSON it would come back from the consumer wrapped in quotes,
        // one more pair every time the message is published again, to a retry or to a dead letter topic
        if (data is string text)
            return Encoding.UTF8.GetBytes(text);

        var json = JsonSerializer.Serialize(data);
        return Encoding.UTF8.GetBytes(json);
    }
}