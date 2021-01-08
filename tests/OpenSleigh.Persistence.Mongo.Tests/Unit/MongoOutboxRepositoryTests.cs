using System;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Persistence.Mongo.Messaging;
using OpenSleigh.Persistence.Mongo.Utils;
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
            var dbCtx = NSubstitute.Substitute.For<IDbContext>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var sut = new MongoOutboxRepository(dbCtx, serializer, MongoOutboxRepositoryOptions.Default);

            var result = await sut.ReadMessagesToProcess();
            result.Should().NotBeNull().And.BeEmpty();
        }
    }
}
