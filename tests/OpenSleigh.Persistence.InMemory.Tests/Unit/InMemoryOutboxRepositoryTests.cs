using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Persistence.InMemory.Messaging;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    public class InMemoryOutboxRepositoryTests
    {
        [Fact]
        public async Task ReadMessagesToProcess_should_return_empty_collection_when_no_messages_available()
        {
            var sut = new InMemoryOutboxRepository();
            var results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_return_pending_messages()
        {
            var messages = new[]
            {
                DummyMessage.New(),
                DummyMessage.New(),
                DummyMessage.New(),
            };
            var sut = new InMemoryOutboxRepository();

            foreach (var message in messages)
                await sut.AppendAsync(message);
            
            var results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull().And.Contain(messages);
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_not_return_sent_messages()
        {
            var messages = new[]
            {
                DummyMessage.New(),
                DummyMessage.New(),
                DummyMessage.New(),
            };
            var sut = new InMemoryOutboxRepository();

            foreach (var message in messages)
                await sut.AppendAsync(message);

            await sut.MarkAsSentAsync(messages[0]);

            var results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull()
                .And.HaveCount(2)
                .And.NotContain(messages[0]);
        }
    }
}
