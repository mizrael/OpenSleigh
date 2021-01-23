using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    internal class SqlTransactionManager : ITransactionManager
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SqlTransactionManager(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        public async Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            //TODO: I don't like this. Consider changing lifetime from Singleton to Scoped
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ISagaDbContext>();
            var transaction = await dbContext.StartTransactionAsync(cancellationToken);
            return transaction;
        }
    }
}