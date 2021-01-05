using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.Messaging
{
    public class DefaultMessageBusTests
    {
        [Fact]
        public async Task PublishAsync_should_throw_if_message_null()
        {
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            var sut = new DefaultMessageBus(repo);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync<StartDummySaga>(null));
        }
        
        [Fact]
        public async Task PublishAsync_should_append_to_outbox()
        {
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            var sut = new DefaultMessageBus(repo);

            var message = StartDummySaga.New();
            await sut.PublishAsync(message);

            await repo.Received(1).AppendAsync(message, null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_should_pass_transaction_if_set()
        {
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            var sut = new DefaultMessageBus(repo);

            var message = StartDummySaga.New();

            var transaction = NSubstitute.Substitute.For<ITransaction>();
            sut.SetTransaction(transaction);
            
            await sut.PublishAsync(message);

            await repo.Received(1).AppendAsync(message, transaction, Arg.Any<CancellationToken>());
        }
    }
}
