using FluentAssertions;
using OpenSleigh.Core.Exceptions;
using MongoDB.Driver;
using NSubstitute;
using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute.ReturnsExtensions;
using Xunit;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.Cosmos.Mongo.Tests.Unit
{
    public class CosmosSagaStateRepositoryTests
    {
        [Fact]
        public async Task ReleaseLockAsync_should_throw_when_release_lock_fails()
        {
            var newState = DummyState.New();

            var coll = NSubstitute.Substitute.For<IMongoCollection<Entities.SagaState>>();
            coll.UpdateOneAsync(Arg.Any<FilterDefinition<Entities.SagaState>>(),
                    Arg.Any<UpdateDefinition<Entities.SagaState>>(),
                    Arg.Any<UpdateOptions>(),
                    Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs((UpdateResult)null);

            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            dbContext.SagaStates.Returns(coll);

            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var options = new CosmosSagaStateRepositoryOptions(TimeSpan.FromMinutes(1));
            var sut = new CosmosSagaStateRepository(dbContext, serializer, options);

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseLockAsync(newState, Guid.NewGuid(), CancellationToken.None));
            ex.Message.Should().Contain("unable to release lock on saga state");
        }

        [Fact]
        public async Task ReleaseLockAsync_should_throw_when_input_null()
        {
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var sut = new CosmosSagaStateRepository(dbContext, serializer, CosmosSagaStateRepositoryOptions.Default);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ReleaseLockAsync<DummyState>(null, Guid.NewGuid()));
        }

        [Fact]
        public async Task ReleaseLockAsync_should_use_transaction_when_available()
        {
            var state = DummyState.New();
            var lockId = Guid.NewGuid();

            var session = NSubstitute.Substitute.For<IClientSessionHandle>();
            var transaction = new CosmosTransaction(session);

            var updateResult = NSubstitute.Substitute.ForPartsOf<UpdateResult>();
            updateResult.MatchedCount.ReturnsForAnyArgs(1);
            
            var repo = NSubstitute.Substitute.For<IMongoCollection<Entities.SagaState>>();
            repo.UpdateOneAsync(session,
                    (FilterDefinition<Entities.SagaState>)null,
                    (UpdateDefinition<Entities.SagaState>)null,
                    (UpdateOptions)null,
                    CancellationToken.None)
                .ReturnsForAnyArgs(updateResult);
            
            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            dbContext.SagaStates.Returns(repo);
            dbContext.Transaction.Returns(transaction);
            
            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var sut = new CosmosSagaStateRepository(dbContext, serializer, CosmosSagaStateRepositoryOptions.Default);

            await sut.ReleaseLockAsync<DummyState>(state, lockId);

            await repo.Received(1)
                .UpdateOneAsync(session,
                    Arg.Any<FilterDefinition<Entities.SagaState>>(),
                    Arg.Any<UpdateDefinition<Entities.SagaState>>(),
                    Arg.Any<UpdateOptions>(),
                    Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ReleaseLockAsync_should_use_not_transaction_when_not_available()
        {
            var state = DummyState.New();
            var lockId = Guid.NewGuid();
            
            var updateResult = NSubstitute.Substitute.ForPartsOf<UpdateResult>();
            updateResult.MatchedCount.ReturnsForAnyArgs(1);

            var repo = NSubstitute.Substitute.For<IMongoCollection<Entities.SagaState>>();
            repo.UpdateOneAsync((FilterDefinition<Entities.SagaState>)null,
                    (UpdateDefinition<Entities.SagaState>)null,
                    (UpdateOptions)null,
                    CancellationToken.None)
                .ReturnsForAnyArgs(updateResult);

            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            dbContext.SagaStates.Returns(repo);
            dbContext.Transaction.ReturnsNull();

            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var sut = new CosmosSagaStateRepository(dbContext, serializer, CosmosSagaStateRepositoryOptions.Default);

            await sut.ReleaseLockAsync<DummyState>(state, lockId);

            await repo.Received(1)
                .UpdateOneAsync(Arg.Any<FilterDefinition<Entities.SagaState>>(),
                    Arg.Any<UpdateDefinition<Entities.SagaState>>(),
                    Arg.Any<UpdateOptions>(),
                    Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ReleaseLockAsync_should_throw_when_update_fails()
        {
            var state = DummyState.New();
            var lockId = Guid.NewGuid();
            
            var updateResult = NSubstitute.Substitute.ForPartsOf<UpdateResult>();
            updateResult.MatchedCount.ReturnsForAnyArgs(0);

            var repo = NSubstitute.Substitute.For<IMongoCollection<Entities.SagaState>>();
            repo.UpdateOneAsync((FilterDefinition<Entities.SagaState>)null,
                    (UpdateDefinition<Entities.SagaState>)null,
                    (UpdateOptions)null,
                    CancellationToken.None)
                .ReturnsForAnyArgs(updateResult);

            var dbContext = NSubstitute.Substitute.For<IDbContext>();
            dbContext.SagaStates.Returns(repo);

            var serializer = NSubstitute.Substitute.For<IPersistenceSerializer>();
            var sut = new CosmosSagaStateRepository(dbContext, serializer, CosmosSagaStateRepositoryOptions.Default);

            await Assert.ThrowsAsync<LockException>(async () =>
                await sut.ReleaseLockAsync<DummyState>(state, lockId));
        }
    }
}
