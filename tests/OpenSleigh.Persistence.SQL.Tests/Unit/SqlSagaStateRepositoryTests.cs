using OpenSleigh.Utils;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Unit
{
    public class SqlSagaStateRepositoryTests
    {        
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var options = SqlSagaStateRepositoryOptions.Default;

            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(null, null, serializer));
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(null, options, null));            
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(dbContext, null, null));
        }

        [Fact]
        public async Task SaveAsync_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var options = SqlSagaStateRepositoryOptions.Default;
            var sut = new SqlSagaStateRepository(dbContext, options, serializer);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.SaveAsync(null, "lorem"));
        }

        [Fact]
        public async Task LockAsync_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var options = SqlSagaStateRepositoryOptions.Default;
            var sut = new SqlSagaStateRepository(dbContext, options, serializer);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.LockAsync(null));
        }
    }
}
