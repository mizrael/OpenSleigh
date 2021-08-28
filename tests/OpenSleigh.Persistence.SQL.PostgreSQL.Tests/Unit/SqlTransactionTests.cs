using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Persistence.SQL.PostgreSQL.Tests.Unit
{
    public class SqlTransactionTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new SqlTransaction(null));
        }

        [Fact]
        public async Task CommitAsync_should_commit_transaction()
        {
            var transaction = NSubstitute.Substitute.For<IDbContextTransaction>();
            var sut = new SqlTransaction(transaction);
            await sut.CommitAsync();
            transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }
        
        [Fact]
        public async Task RollbackAsync_should_rollback_transaction()
        {
            var transaction = NSubstitute.Substitute.For<IDbContextTransaction>();
            var sut = new SqlTransaction(transaction);
            await sut.RollbackAsync();
            transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        }
        
        [Fact]
        public void Dispose_should_dispose_transaction()
        {
            var transaction = NSubstitute.Substitute.For<IDbContextTransaction>();
            var sut = new SqlTransaction(transaction);
            sut.Dispose();
            transaction.Received(1).Dispose();
        }
    }
}