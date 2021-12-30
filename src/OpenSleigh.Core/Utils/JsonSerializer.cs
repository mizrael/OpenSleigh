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

        public async ValueTask<byte[]> SerializeAsync<T>(T data, CancellationToken cancellationToken = default)
        {
            var type = data.GetType();
            using var ms = new MemoryStream();
            await System.Text.Json.JsonSerializer.SerializeAsync(ms, data, type, Settings, cancellationToken);
            ms.Position = 0;
            var bytes = ms.ToArray();
            return bytes;
        }

        public ValueTask<T> DeserializeAsync<T>(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<T>(data.Span, options: Settings);
            return ValueTask.FromResult(result);
        }

        public object Deserialize(ReadOnlyMemory<byte> data, Type type)
            => System.Text.Json.JsonSerializer.Deserialize(data.Span, type, options: Settings);
    }
}