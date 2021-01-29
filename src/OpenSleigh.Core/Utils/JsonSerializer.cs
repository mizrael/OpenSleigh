using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OpenSleigh.Core.Utils
{
    /// <summary>
    /// can't use System.Text.Json, polymorfic support is not mature: https://github.com/dotnet/runtime/issues/45189
    /// </summary>
    public class JsonSerializer : ISerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        public ValueTask<byte[]> SerializeAsync<T>(T state, CancellationToken cancellationToken = default)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(state, Settings);
            return ValueTask.FromResult(Encoding.UTF8.GetBytes(json));
        }

        public ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default)
        {
            var json = Encoding.UTF8.GetString(data);
            return ValueTask.FromResult(Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, Settings));
        }

        public object Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            var json = System.Text.Encoding.UTF8.GetString(data.Span);
            return Newtonsoft.Json.JsonConvert.DeserializeObject(json, type, Settings);
        }
            
    }

}