using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.Messaging
{
    public class OutboxCleanerTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            Assert.Throws<ArgumentNullException>(() => new OutboxCleaner(null));
        }
        
        [Fact]
        public async Task RunCleanupAsync_should_cleanup_messages()
        {
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            var sut = new OutboxCleaner(repo);
            
            var token = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            
            try
            {
                await sut.RunCleanupAsync(token.Token);
            }
            catch (TaskCanceledException) { }

            await repo.Received().CleanProcessedAsync(Arg.Any<CancellationToken>());
        }
    }
}
