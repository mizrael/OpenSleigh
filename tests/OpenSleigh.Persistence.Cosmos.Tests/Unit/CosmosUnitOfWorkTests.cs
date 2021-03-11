using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.Tests.Unit
{
    public class CosmosUnitOfWorkTests
    {

        [Fact]
        public async Task StartTransactionAsync_should_return_NullTransaction()
        {
            var sut = new CosmosTransactionManager();

            var result = await sut.StartTransactionAsync();
            result.Should().BeOfType<NullTransaction>();
        }
    }
}
