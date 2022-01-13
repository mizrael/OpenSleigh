using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.InMemory.Messaging
{
    public static class ChannelReaderExtensions
    {
        public static async Task<IEnumerable<T>> ReadMultipleAsync<T>(this ChannelReader<T> reader, int maxBatchSize, CancellationToken cancellationToken)
        {
            await reader.WaitToReadAsync(cancellationToken);

            var batch = new List<T>(maxBatchSize);

            while (batch.Count < maxBatchSize && reader.TryRead(out T message) && message is not null)
            {
                batch.Add(message);
            }

            return batch;
        }
    }
}