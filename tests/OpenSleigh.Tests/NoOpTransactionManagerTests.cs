using OpenSleigh.Persistence;

namespace OpenSleigh.Tests;

public class NoOpTransactionManagerTests
{
    [Fact]
    public async Task StartTransactionAsync_should_return_NoOpTransaction()
    {
        var sut = new NoOpTransactionManager();

        var result = await sut.StartTransactionAsync(CancellationToken.None);

        Assert.Same(NoOpTransaction.Instance, result);
    }
}
