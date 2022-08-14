using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Tests.Sagas;
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

            await repo.Received(1)
                     .AppendAsync(
                        Arg.Is<IEnumerable<IMessage>>(msg => msg.Contains(message)), 
                        Arg.Any<CancellationToken>());
        }
    }
}
