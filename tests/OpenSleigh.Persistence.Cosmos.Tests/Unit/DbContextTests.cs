using System;
using FluentAssertions;
using MongoDB.Driver;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.Tests.Unit
{
    public class DbContextTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new DbContext(null));
        }

        [Fact]
        public void ctor_should_build_valid_instance()
        {
            var db = NSubstitute.Substitute.For<IMongoDatabase>();

            var outboxColl = NSubstitute.Substitute.For<IMongoCollection<Entities.OutboxMessage>>();
            outboxColl.CollectionNamespace.ReturnsForAnyArgs(CollectionNamespace.FromFullName("db.outbox"));
            db.GetCollection<Entities.OutboxMessage>(null)
                .ReturnsForAnyArgs(outboxColl);

            var sagaStatesColl = NSubstitute.Substitute.For<IMongoCollection<Entities.SagaState>>();
            sagaStatesColl.CollectionNamespace.ReturnsForAnyArgs(CollectionNamespace.FromFullName("db.sagaStates"));
            db.GetCollection<Entities.SagaState>(null)
                .ReturnsForAnyArgs(sagaStatesColl);

            var sut = new DbContext(db);

            sut.Outbox.Should().NotBeNull();
            sut.Outbox.CollectionNamespace.Should().NotBeNull();
            sut.Outbox.CollectionNamespace.CollectionName.Should().Be("outbox");

            sut.SagaStates.Should().NotBeNull();
            sut.SagaStates.CollectionNamespace.Should().NotBeNull();
            sut.SagaStates.CollectionNamespace.CollectionName.Should().Be("sagaStates");
        }
    }
}
