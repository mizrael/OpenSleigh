using System;
using System.Threading.Tasks;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Persistence.SQL.SQLServer.Tests.Unit
{
    public class SqlSagaStateRepositoryTests
    {
        
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var options = SqlSagaStateRepositoryOptions.Default;
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(null, null, options));
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(null, serializer, null));
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(dbContext, null, null));
        }

        [Fact]
        public async Task ReleaseLockAsync_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var options = SqlSagaStateRepositoryOptions.Default;
            var sut = new SqlSagaStateRepository(dbContext, serializer, options);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ReleaseLockAsync<DummyState>(null, Guid.NewGuid()));
        }
    }
}
