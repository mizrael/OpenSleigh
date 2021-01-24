using System;
using System.Threading.Tasks;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Unit
{
    public class SqlOutboxRepositoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(null, serializer));
            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(dbContext, null));
        }

        [Fact]
        public async Task LockAsync_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var sut = new SqlOutboxRepository(dbContext, serializer);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.LockAsync(null));
        }

        [Fact]
        public async Task AppendAsync_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var sut = new SqlOutboxRepository(dbContext, serializer);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.AppendAsync(null));
        }
    }
}
