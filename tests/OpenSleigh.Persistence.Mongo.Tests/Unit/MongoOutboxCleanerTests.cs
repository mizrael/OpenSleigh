using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Persistence.Mongo.Messaging;
using Xunit;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit
{
    public class MongoOutboxCleanerTests
    {
        [Fact]
        public async Task StartAsync_should_cleanup_messages()
        {
            var repo = NSubstitute.Substitute.For<IOutboxRepository>();
            var options = new MongoOutboxCleanerOptions(TimeSpan.FromSeconds(2));
            var sut = new MongoOutboxCleaner(repo, options);

            var token = new CancellationTokenSource(options.Interval);
            
            try
            {
                await sut.StartAsync(token.Token);
            }
            catch (TaskCanceledException) { }

            await repo.Received().CleanProcessedAsync(Arg.Any<CancellationToken>());
        }
    }
}
