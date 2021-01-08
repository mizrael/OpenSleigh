using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory.Messaging;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    public class InMemorySubscriberTests
    {
        [Fact]
        public void ctor_should_throw_when_arguments_null()
        {
            var processor = NSubstitute.Substitute.For<IMessageProcessor>();
            var reader = NSubstitute.Substitute.For<ChannelReader<DummyMessage>>();

            Assert.Throws<ArgumentNullException>(() => new InMemorySubscriber<DummyMessage>(null, reader));
            Assert.Throws<ArgumentNullException>(() => new InMemorySubscriber<DummyMessage>(processor, null));
        }

        [Fact]
        public async Task StartAsync_should_consume_messages()
        {
            var processor = NSubstitute.Substitute.For<IMessageProcessor>();

            var messages = new[]
            {
                DummyMessage.New(),
                DummyMessage.New(),
            };
            var asyncMessages = GetMessagesAsync(messages);

            var reader = NSubstitute.Substitute.For<ChannelReader<DummyMessage>>();
            reader.ReadAllAsync()
                .ReturnsForAnyArgs(asyncMessages);

            var sut = new InMemorySubscriber<DummyMessage>(processor, reader);
            await sut.StartAsync();

            foreach(var message in messages)
                await processor.Received(1).ProcessAsync(message);
        }

        private static async IAsyncEnumerable<DummyMessage> GetMessagesAsync(IEnumerable<DummyMessage> messages)
        {
            foreach (var message in messages)
                yield return message;
            await Task.CompletedTask; 
        }

        [Fact]
        public void StopAsync_should_do_nothing() // just to make Sonarcloud happy
        {
            var processor = NSubstitute.Substitute.For<IMessageProcessor>();
            var reader = NSubstitute.Substitute.For<ChannelReader<DummyMessage>>();
            var sut = new InMemorySubscriber<DummyMessage>(processor, reader);
            var result = sut.StopAsync();
            result.Should().Be(Task.CompletedTask);
        }
    }
}
