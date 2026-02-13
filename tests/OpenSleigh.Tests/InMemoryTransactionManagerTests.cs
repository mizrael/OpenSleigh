using OpenSleigh.InMemory;
using OpenSleigh.Persistence;

namespace OpenSleigh.Tests;

public class InMemoryTransactionManagerTests
{
    [Fact]
    public async Task StartTransactionAsync_should_return_NoOpTransaction()
    {
        var sut = new InMemoryTransactionManager();

        var result = await sut.StartTransactionAsync(CancellationToken.None);

        Assert.IsType<NoOpTransaction>(result);
        Assert.Same(NoOpTransaction.Instance, result);
    }
}
