using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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
            var logger = NSubstitute.Substitute.For<ILogger<InMemorySubscriber<DummyMessage>>>();

            Assert.Throws<ArgumentNullException>(() => new InMemorySubscriber<DummyMessage>(null, reader, logger));
            Assert.Throws<ArgumentNullException>(() => new InMemorySubscriber<DummyMessage>(processor, null, logger));
            Assert.Throws<ArgumentNullException>(() => new InMemorySubscriber<DummyMessage>(processor, reader, null));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(5, 1)]
        [InlineData(5, 9)]
        public async Task StartAsync_should_consume_messages(int messagesCount, int maxBatchSize)
        {
            var processor = NSubstitute.Substitute.For<IMessageProcessor>();

            var messages = Enumerable.Repeat(1, messagesCount)
                .Select(_ => DummyMessage.New())
                .ToArray();
            
            var channel = Channel.CreateUnbounded<DummyMessage>();

            var logger = NSubstitute.Substitute.For<ILogger<InMemorySubscriber<DummyMessage>>>();

            var ts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var sut = new InMemorySubscriber<DummyMessage>(processor, channel.Reader, logger, new InMemorySubscriberOptions(maxBatchSize));
            await sut.StartAsync(ts.Token);

            foreach (var message in messages)
                await channel.Writer.WriteAsync(message);

            while (!ts.IsCancellationRequested)
                await Task.Delay(10);

            foreach (var message in messages)
                await processor.Received(1).ProcessAsync(message, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_log_exceptions()
        {
            var messages = new[]
            {
                DummyMessage.New(),
            };
            
            var processor = NSubstitute.Substitute.For<IMessageProcessor>();
            var ex = new Exception("whoops");
            processor.When(p => p.ProcessAsync(messages[0], Arg.Any<CancellationToken>()))
                .Throw(ex);

            var channel = Channel.CreateUnbounded<DummyMessage>();

            var logger = new FakeLogger<InMemorySubscriber<DummyMessage>>();

            var ts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            var sut = new InMemorySubscriber<DummyMessage>(processor, channel.Reader, logger);
            await sut.StartAsync(ts.Token);

            foreach (var message in messages)
                await channel.Writer.WriteAsync(message);

            while (!ts.IsCancellationRequested)
                await Task.Delay(10);

            logger.Logs.Count.Should().Be(1);
            logger.Logs.Should().Contain(l => l.ex == ex);
        }

        [Fact]
        public void StopAsync_should_do_nothing() // just to make Sonarcloud happy
        {
            var processor = NSubstitute.Substitute.For<IMessageProcessor>();
            var reader = NSubstitute.Substitute.For<ChannelReader<DummyMessage>>();
            var logger = NSubstitute.Substitute.For<ILogger<InMemorySubscriber<DummyMessage>>>();
            var sut = new InMemorySubscriber<DummyMessage>(processor, reader, logger);
            var result = sut.StopAsync();
            result.Should().Be(Task.CompletedTask);
        }
    }
}
