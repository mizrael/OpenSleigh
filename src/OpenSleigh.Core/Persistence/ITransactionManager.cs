using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Persistence
{
    public interface ITransactionManager
    {
        Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default);
    }
}