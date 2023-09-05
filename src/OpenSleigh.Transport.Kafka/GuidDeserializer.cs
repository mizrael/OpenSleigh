using System.Runtime.CompilerServices;
using Confluent.Kafka;

[assembly: InternalsVisibleTo("OpenSleigh.Transport.Kafka.Tests")]
namespace OpenSleigh.Transport.Kafka
{
    internal class GuidDeserializer : IDeserializer<Guid>
    {
        public Guid Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            return new Guid(data);
        }
    }
}