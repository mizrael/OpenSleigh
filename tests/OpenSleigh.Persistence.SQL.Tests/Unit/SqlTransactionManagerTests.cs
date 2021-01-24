using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Unit
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
            
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(ISagaDbContext)).ReturnsForAnyArgs(dbCtx);
            
            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.ReturnsForAnyArgs(sp);
            
            var factory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            factory.CreateScope().Returns(scope);
            
            var sut = new SqlTransactionManager(factory);

            var transaction = await sut.StartTransactionAsync(default);
            transaction.Should().Be(expectedTransaction);
        }
    }
}