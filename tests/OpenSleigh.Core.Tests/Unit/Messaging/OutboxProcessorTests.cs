using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.Messaging
{
    public class OutboxProcessorTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            var publisher = NSubstitute.Substitute.For<IPublisher>();
            var options = OutboxProcessorOptions.Default;
            var logger = NSubstitute.Substitute.For<ILogger<OutboxProcessor>>();
            
            Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(null, publisher, options, logger));
            Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(repo, null, options, logger));
            Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(repo, publisher, null, logger));
            Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(repo, publisher, options, null));
        }

        [Fact]
        public async Task StartAsync_should_do_nothing_when_no_pending_messages_available()
        {
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            var publisher = NSubstitute.Substitute.For<IPublisher>();
            var options = new OutboxProcessorOptions(TimeSpan.FromSeconds(2));
            var logger = NSubstitute.Substitute.For<ILogger<OutboxProcessor>>();

            var sut = new OutboxProcessor(repo, publisher, options, logger);

            var token = new CancellationTokenSource(options.Interval);

            try
            {
                await sut.StartAsync(token.Token);
            }
            catch (TaskCanceledException) { }

            await repo.Received().ReadMessagesToProcess(Arg.Any<CancellationToken>());
            await publisher.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<IMessage>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_process_pending_messages_()
        {
            var messages = new[]
            {
                StartDummySaga.New(),
                StartDummySaga.New(),
                StartDummySaga.New()
            };
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            repo.ReadMessagesToProcess(Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(messages);
            
            var publisher = NSubstitute.Substitute.For<IPublisher>();
            var options = new OutboxProcessorOptions(TimeSpan.FromSeconds(2));
            var logger = NSubstitute.Substitute.For<ILogger<OutboxProcessor>>();

            var sut = new OutboxProcessor(repo, publisher, options, logger);

            var token = new CancellationTokenSource(options.Interval);

            try
            {
                await sut.StartAsync(token.Token);
            }
            catch (TaskCanceledException) { }

            await repo.Received().ReadMessagesToProcess(Arg.Any<CancellationToken>());

            foreach (var message in messages)
            {
                await publisher.Received(1)
                    .PublishAsync(message, Arg.Any<CancellationToken>());
                await repo.ReleaseAsync(message, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
            }
        }
    }
}
