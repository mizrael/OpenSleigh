using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Utils
{
    public class JsonSerializer : ITransportSerializer, IPersistenceSerializer
    {
        private static readonly JsonSerializerOptions Settings = new ()
        {
            IgnoreReadOnlyFields = false,
            IgnoreReadOnlyProperties = false,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true            
        };

        public byte[] Serialize<T>(T data)
        {
            var type = data.GetType();
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, type, Settings);            
        }

        public ValueTask<T> DeserializeAsync<T>(Stream data, CancellationToken cancellationToken = default)
            => System.Text.Json.JsonSerializer.DeserializeAsync<T>(data, options: Settings);

        public T Deserialize<T>(byte[] data)
            => System.Text.Json.JsonSerializer.Deserialize<T>(data, options: Settings);

        public object Deserialize(byte[] data, Type type)
            => System.Text.Json.JsonSerializer.Deserialize(data, type, options: Settings);
    }
}