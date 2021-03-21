using System;
using System.Threading.Tasks;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.Unit
{
    public class CosmosSqlSagaStateRepositoryTests
    {
        
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var options = CosmosSqlSagaStateRepositoryOptions.Default;
            Assert.Throws<ArgumentNullException>(() => new CosmosSqlSagaStateRepository(null, null, options));
            Assert.Throws<ArgumentNullException>(() => new CosmosSqlSagaStateRepository(null, serializer, null));
            Assert.Throws<ArgumentNullException>(() => new CosmosSqlSagaStateRepository(dbContext, null, null));
        }

        [Fact]
        public async Task ReleaseLockAsync_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var options = CosmosSqlSagaStateRepositoryOptions.Default;
            var sut = new CosmosSqlSagaStateRepository(dbContext, serializer, options);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ReleaseLockAsync<DummyState>(null, Guid.NewGuid()));
        }
    }
}
