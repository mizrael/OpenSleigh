using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Persistence
{
    public interface IUnitOfWork
    {
        Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default);
    }
}