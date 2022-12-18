using OpenSleigh.Utils;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Unit
{
    public class SqlOutboxRepositoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();

            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(null, resolver, SqlOutboxRepositoryOptions.Default));
            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(dbContext, resolver, null));
            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(dbContext, null, SqlOutboxRepositoryOptions.Default));
        }

        [Fact]
        public async Task LockAsync_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();

            var sut = new SqlOutboxRepository(dbContext, resolver, SqlOutboxRepositoryOptions.Default);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.LockAsync(null));
        }

        [Fact]
        public async Task AppendAsync_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();

            var sut = new SqlOutboxRepository(dbContext, resolver, SqlOutboxRepositoryOptions.Default);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.AppendAsync(null));
        }

        [Fact]
        public async Task DeleteAsync_should_throw_if_message_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var resolver = NSubstitute.Substitute.For<ITypeResolver>();

            var sut = new SqlOutboxRepository(dbContext, resolver, SqlOutboxRepositoryOptions.Default);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.DeleteAsync(null, "lorem"));
        }
    }
}
