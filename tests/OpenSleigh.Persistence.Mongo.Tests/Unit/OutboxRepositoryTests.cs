using System;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Persistence.Mongo.Messaging;
using OpenSleigh.Persistence.Mongo.Utils;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit
{
    public class OutboxRepositoryTests
    {
        [Fact]
        public void ctor_should_throw_when_DbContext_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            Assert.Throws<ArgumentNullException>(() => new OutboxRepository(null, serializer));
        }

        [Fact]
        public void ctor_should_throw_when_Serializer_null()
        {
            var dbCtx = NSubstitute.Substitute.For<IDbContext>();
            Assert.Throws<ArgumentNullException>(() => new OutboxRepository(dbCtx, null));
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_return_empty_collection_if_no_data_available()
        {
            var dbCtx = NSubstitute.Substitute.For<IDbContext>();
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var sut = new OutboxRepository(dbCtx, serializer);

            var result = await sut.ReadMessagesToProcess();
            result.Should().NotBeNull().And.BeEmpty();
        }
    }
}
