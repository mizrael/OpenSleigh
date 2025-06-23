namespace OpenSleigh.Persistence.SQL;

public class SqlTransactionManager : ITransactionManager
{
    private readonly SagaDbContext _dbContext;

    public SqlTransactionManager(SagaDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);
        return transaction;
    }
}
