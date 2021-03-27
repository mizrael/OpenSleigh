using FluentAssertions;
using OpenSleigh.Persistence.Cosmos.SQL.Entities;
using System;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.Unit.Entities
{
    public class OutboxMessageTests
	{
		[Fact]
		public void New_should_create_valid_instance()
		{
			var expectedId = Guid.NewGuid();
			var expectedType = "lorem";
			var expectedData = new byte[] { 1, 2, 3, 4 };
			var expectedCorrelationId = Guid.NewGuid();
			var sut = OutboxMessage.New(expectedId, expectedData, expectedType, expectedCorrelationId);
			sut.Id.Should().Be(expectedId);
			sut.Data.Should().BeEquivalentTo(expectedData);
			sut.Type.Should().Be(expectedType);
			sut.Status.Should().Be(OutboxMessage.MessageStatuses.Pending);
			sut.PartitionKey.Should().Be(expectedCorrelationId.ToString());
			sut.PublishingDate.Should().BeNull();
			sut.LockId.Should().BeNull();
			sut.LockTime.Should().BeNull();
		}

		[Fact]
		public void Lock_should_set_lockId_and_date()
		{
			var sut = OutboxMessage.New(Guid.NewGuid(), null, "lorem", Guid.NewGuid());
			sut.Lock();
			sut.LockId.Should().NotBeNull();
			sut.LockTime.Should().NotBeNull();
		}

		[Fact]
		public void Release_should_release_lock()
		{
			var sut = OutboxMessage.New(Guid.NewGuid(), null, "lorem", Guid.NewGuid());
			sut.Release();
			sut.Status.Should().Be(OutboxMessage.MessageStatuses.Processed);			
			sut.PublishingDate.Should().NotBeNull();
			sut.LockId.Should().BeNull();
			sut.LockTime.Should().BeNull();
		}
	}
}
