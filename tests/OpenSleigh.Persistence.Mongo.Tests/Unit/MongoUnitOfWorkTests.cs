using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NSubstitute;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit
{
    public class MongoUnitOfWorkTests
    {
        [Fact]
        public void ctor_should_throw_when_arguments_null()
        {
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            var client = NSubstitute.Substitute.For<IMongoClient>();
            var logger = NSubstitute.Substitute.For<ILogger<MongoTransactionManager>>();

            Assert.Throws<ArgumentNullException>(() => new MongoTransactionManager(null, logger, dbContext));
            Assert.Throws<ArgumentNullException>(() => new MongoTransactionManager(client, null, dbContext));
            Assert.Throws<ArgumentNullException>(() => new MongoTransactionManager(client, logger, null));
        }
        
        [Fact]
        public async Task StartTransactionAsync_should_return_NullTransaction_if_transactions_not_supported()
        {
            var session = NSubstitute.Substitute.For<IClientSessionHandle>();
            session.When(s => s.StartTransaction(Arg.Any<TransactionOptions>()))
                .Throw<NotSupportedException>();
            var client = NSubstitute.Substitute.For<IMongoClient>();
            client.StartSession()
                .ReturnsForAnyArgs(session);
            
            var logger = NSubstitute.Substitute.For<ILogger<MongoTransactionManager>>();
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            var sut = new MongoTransactionManager(client, logger, dbContext);

            var result = await sut.StartTransactionAsync();
            result.Should().BeOfType<NullTransaction>();
        }

        [Fact]
        public async Task StartTransactionAsync_should_return_MongoTransaction_if_transactions_supported()
        {
            var session = NSubstitute.Substitute.For<IClientSessionHandle>();
            var client = NSubstitute.Substitute.For<IMongoClient>();
            client.StartSession()
                .ReturnsForAnyArgs(session);

            var logger = NSubstitute.Substitute.For<ILogger<MongoTransactionManager>>();
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            var sut = new MongoTransactionManager(client, logger, dbContext);

            var result = await sut.StartTransactionAsync();
            result.Should().BeOfType<MongoTransaction>();
        }
    }
}
