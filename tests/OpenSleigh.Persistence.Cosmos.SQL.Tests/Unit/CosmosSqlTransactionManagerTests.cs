using System;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.Unit
{
    public class CosmosSqlTransactionManagerTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new CosmosSqlTransactionManager(null));
        }

        [Fact]
        public async Task StartTransactionAsync_should_return_transaction()
        {
            var expectedTransaction = NSubstitute.Substitute.For<ITransaction>();
            
            var dbCtx = NSubstitute.Substitute.For<ISagaDbContext>();
            dbCtx.StartTransactionAsync(default).ReturnsForAnyArgs(expectedTransaction);

            var sut = new CosmosSqlTransactionManager(dbCtx);

            var transaction = await sut.StartTransactionAsync(default);
            transaction.Should().Be(expectedTransaction);
        }
    }
}