using Newtonsoft.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Mongo
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

        public async ValueTask<byte[]> SerializeAsync<T>(T state, CancellationToken cancellationToken = default)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(state, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public async ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default)
        {
            var json = Encoding.UTF8.GetString(data);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }

}