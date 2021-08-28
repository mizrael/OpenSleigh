using System;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Persistence.SQL.PostgreSQL.Tests.Unit
{
    public class SqlTransactionManagerTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new SqlTransactionManager(null));
        }

        [Fact]
        public async Task StartTransactionAsync_should_return_transaction()
        {
            var expectedTransaction = NSubstitute.Substitute.For<ITransaction>();
            
            var dbCtx = NSubstitute.Substitute.For<ISagaDbContext>();
            dbCtx.StartTransactionAsync(default).ReturnsForAnyArgs(expectedTransaction);

            var sut = new SqlTransactionManager(dbCtx);

            var transaction = await sut.StartTransactionAsync(default);
            transaction.Should().Be(expectedTransaction);
        }
    }
}