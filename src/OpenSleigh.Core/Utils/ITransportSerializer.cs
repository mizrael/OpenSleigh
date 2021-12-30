using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Utils
{
    public interface ITransportSerializer
    {
        byte[] Serialize<T>(T data);
        ValueTask<T> DeserializeAsync<T>(Stream data, CancellationToken cancellationToken = default);
        object Deserialize(byte[] data, Type type); 
    }
}