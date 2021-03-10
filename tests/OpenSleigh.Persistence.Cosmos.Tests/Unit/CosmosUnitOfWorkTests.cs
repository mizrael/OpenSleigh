using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NSubstitute;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.Tests.Unit
{
    public class CosmosUnitOfWorkTests
    {
        [Fact]
        public void ctor_should_throw_when_arguments_null()
        {
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            var client = NSubstitute.Substitute.For<IMongoClient>();
            var logger = NSubstitute.Substitute.For<ILogger<CosmosTransactionManager>>();

            Assert.Throws<ArgumentNullException>(() => new CosmosTransactionManager(null, logger, dbContext));
            Assert.Throws<ArgumentNullException>(() => new CosmosTransactionManager(client, null, dbContext));
            Assert.Throws<ArgumentNullException>(() => new CosmosTransactionManager(client, logger, null));
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
            
            var logger = NSubstitute.Substitute.For<ILogger<CosmosTransactionManager>>();
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            var sut = new CosmosTransactionManager(client, logger, dbContext);

            var result = await sut.StartTransactionAsync();
            result.Should().BeOfType<NullTransaction>();
        }

        [Fact(Skip = "there is still no support for multi-document transactions across collections")]        
        public async Task StartTransactionAsync_should_return_MongoTransaction_if_transactions_supported()
        {
            var session = NSubstitute.Substitute.For<IClientSessionHandle>();
            var client = NSubstitute.Substitute.For<IMongoClient>();
            client.StartSession()
                .ReturnsForAnyArgs(session);

            var logger = NSubstitute.Substitute.For<ILogger<CosmosTransactionManager>>();
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            var sut = new CosmosTransactionManager(client, logger, dbContext);

            var result = await sut.StartTransactionAsync();
            result.Should().BeOfType<CosmosTransaction>();
        }
    }
}
