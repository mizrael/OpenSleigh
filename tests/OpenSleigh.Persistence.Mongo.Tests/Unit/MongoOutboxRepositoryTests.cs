using System;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit
{
    public class MongoOutboxRepositoryTests
    {
        [Fact]
        public void ctor_should_throw_when_DbContext_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            Assert.Throws<ArgumentNullException>(() => new MongoOutboxRepository(null, serializer, MongoOutboxRepositoryOptions.Default));
        }

        [Fact]
        public void ctor_should_throw_when_Serializer_null()
        {
            var dbCtx = NSubstitute.Substitute.For<IDbContext>();
            Assert.Throws<ArgumentNullException>(() => new MongoOutboxRepository(dbCtx, null, MongoOutboxRepositoryOptions.Default));
        }

        [Fact]
        public void ctor_should_throw_when_options_null()
        {
            var dbCtx = NSubstitute.Substitute.For<IDbContext>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            Assert.Throws<ArgumentNullException>(() => new MongoOutboxRepository(dbCtx, serializer, null));
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_return_empty_collection_if_no_data_available()
        {
            var sut = CreateSut();

            var result = await sut.ReadMessagesToProcess();
            result.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public async Task ReleaseAsync_should_throw_if_message_null()
        {
            var sut = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ReleaseAsync(null, Guid.Empty));
        }

        [Fact]
        public async Task AppendAsync_should_throw_if_message_null()
        {
            var sut = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.AppendAsync(null));
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_null()
        {
            var sut = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.LockAsync(null));
        }

        private static MongoOutboxRepository CreateSut()
        {
            var dbCtx = NSubstitute.Substitute.For<IDbContext>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var sut = new MongoOutboxRepository(dbCtx, serializer, MongoOutboxRepositoryOptions.Default);
            return sut;
        }
    }
}
