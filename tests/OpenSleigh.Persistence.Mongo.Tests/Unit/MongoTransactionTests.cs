using System;
using System.Threading.Tasks;
using MongoDB.Driver;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit
{
    public class MongoTransactionTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            Assert.Throws<ArgumentNullException>(() => new MongoTransaction(null));
        }

        [Fact]
        public async Task CommitAsync_should_commit_transaction()
        {
            var session = NSubstitute.Substitute.For<IClientSessionHandle>();
            var sut = new MongoTransaction(session);
            
            await sut.CommitAsync();
            await session.Received(1).CommitTransactionAsync();
        }

        [Fact]
        public async Task RollbackAsync_should_rollback_transaction()
        {
            var session = NSubstitute.Substitute.For<IClientSessionHandle>();
            var sut = new MongoTransaction(session);

            await sut.RollbackAsync();
            await session.Received(1).AbortTransactionAsync();
        }

        [Fact]
        public void Dispose_should_dispose_transaction()
        {
            var session = NSubstitute.Substitute.For<IClientSessionHandle>();
            var sut = new MongoTransaction(session);

            sut.Dispose();
            session.Received(1).Dispose();
        }
    }
}
