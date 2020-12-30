using OpenSleigh.Core;
using Newtonsoft.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Mongo
{
    /// <summary>
    /// can't use System.Text.Json, polymorfic support is not mature: https://github.com/dotnet/runtime/issues/45189
    /// </summary>
    public class JsonSagaStateSerializer : ISagaStateSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        public async Task<byte[]> SerializeAsync<TD>(TD state, CancellationToken cancellationToken = default) where TD : SagaState
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(state, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public async Task<TD> DeserializeAsync<TD>(byte[] data, CancellationToken cancellationToken = default) where TD : SagaState
        {
            var json = Encoding.UTF8.GetString(data);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<TD>(json, Settings);
        }
    }

}