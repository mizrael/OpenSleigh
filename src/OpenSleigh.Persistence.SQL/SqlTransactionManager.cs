namespace OpenSleigh.Persistence.SQL;

public class SqlTransactionManager : ITransactionManager
{
    public SqlTransactionManager(SagaDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// the context the transactions run on: what has to take part in a transaction has to use it.
    /// </summary>
    public SagaDbContext DbContext { get; }

    public async ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await DbContext.BeginTransactionAsync(cancellationToken);
        return transaction;
    }
}
