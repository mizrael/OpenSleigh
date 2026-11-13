using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration;

/// <summary>
/// the outbox relay on a container configured through UseMongoPersistence only, plus a publisher:
/// what a host on MongoDB with any transport runs.
/// </summary>
[Category("Integration")]
[Trait("Category", "Integration")]
public class MongoOutboxRelayTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public MongoOutboxRelayTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_relay_should_publish_the_due_messages_and_leave_the_others_in_the_outbox()
    {
        var publisher = new RecordingPublisher();
        await using var provider = BuildProvider(publisher);
        var systemInfo = provider.GetRequiredService<ISystemInfo>();
        var due = new[] { MessageEnvelope.Create(new FakeMessage(), systemInfo), MessageEnvelope.Create(new FakeMessage(), systemInfo) };
        var notDue = MessageEnvelope.Create(new FakeMessage(), systemInfo, TimeSpan.FromHours(1));

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().AppendAsync([.. due, notDue]);

        OutboxRelayResult result;
        do
        {
            await using var relay = provider.CreateAsyncScope();
            result = await relay.ServiceProvider.GetRequiredService<IOutboxProcessor>().RelayPendingMessagesAsync();
            Assert.Equal(0, result.Failed);
        } while (result.Relayed > 0);

        Assert.Equal(
            due.Select(message => message.MessageId).OrderBy(id => id),
            publisher.PublishedIds.OrderBy(id => id));

        var left = await provider.GetRequiredService<IDbContext>().OutboxMessages
            .Find(Builders<Entities.OutboxMessage>.Filter.Empty)
            .ToListAsync();
        Assert.Equal(notDue.MessageId, Assert.Single(left).MessageId);
    }

    private ServiceProvider BuildProvider(IPublisher publisher)
    {
        var configuration = new MongoConfiguration(_fixture.ConnectionString, _fixture.CreateDatabaseName());

        var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(publisher)
            .AddOpenSleigh(cfg => cfg.UseMongoPersistence(configuration))
            .BuildServiceProvider();
        provider.GetRequiredService<ITypeResolver>().Register(typeof(FakeMessage));
        return provider;
    }

    private sealed class RecordingPublisher : IPublisher
    {
        private readonly ConcurrentQueue<string> _publishedIds = new();

        public IReadOnlyCollection<string> PublishedIds => _publishedIds;

        public ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _publishedIds.Enqueue(envelope.MessageId);
            return ValueTask.CompletedTask;
        }
    }
}
