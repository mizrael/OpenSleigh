using System;
using System.Text.Json;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class JsonEncoder : IEncoder, IDecoder
    {
        private readonly JsonSerializerOptions _serializerOptions;

        public JsonEncoder(JsonSerializerOptions serializerOptions = null)
        {
            _serializerOptions = serializerOptions ?? new JsonSerializerOptions();
        }

        public EncodedData Encode<T>(T data)
        {
            if (null == data)
                throw new ArgumentNullException(nameof(data));

            var encoded = JsonSerializer.SerializeToUtf8Bytes<T>(data, _serializerOptions);
            return new EncodedData(encoded);
        }

        public object Decode(ReadOnlyMemory<byte> data, Type type) => JsonSerializer.Deserialize(data.Span, type, _serializerOptions);
    }
}