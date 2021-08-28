using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Unit
{
    public class SqlOutboxRepositoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var options = SqlOutboxRepositoryOptions.Default;
            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(null, serializer, options));
            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(dbContext, null, options));
            Assert.Throws<ArgumentNullException>(() => new SqlOutboxRepository(dbContext, serializer, null));
        }

        [Fact]
        public async Task LockAsync_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var sut = new SqlOutboxRepository(dbContext, serializer, SqlOutboxRepositoryOptions.Default);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.LockAsync(null));
        }

        [Fact]
        public async Task AppendAsync_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var sut = new SqlOutboxRepository(dbContext, serializer, SqlOutboxRepositoryOptions.Default);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.AppendAsync(null));
        }

        [Fact]
        public async Task ReleaseAsync_should_throw_if_message_null()
        {
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var sut = new SqlOutboxRepository(dbContext, serializer, SqlOutboxRepositoryOptions.Default);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ReleaseAsync(null, Guid.Empty));
        }

        [Fact]
        public async Task CleanProcessedAsync_should_rollback_transaction_when_an_exception_occurs_and_rethrow()
        {
            var transaction = NSubstitute.Substitute.For<ITransaction>();
            
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            dbContext.StartTransactionAsync(default)
                .ReturnsForAnyArgs(transaction);
            
            var expectedException = new Exception("whoops");
            dbContext.OutboxMessages.ThrowsForAnyArgs(expectedException);

            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            
            var sut = new SqlOutboxRepository(dbContext, serializer, SqlOutboxRepositoryOptions.Default);
            var ex = await Assert.ThrowsAsync<Exception>(async () => await sut.CleanProcessedAsync());
            ex.Should().Be(expectedException);

            await transaction.Received(1)
                .RollbackAsync(Arg.Any<CancellationToken>());
        }
    }
}
