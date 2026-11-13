using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

public class OutboxMessageTests
{
    private static readonly JsonSerializer Serializer = new();

    [Fact]
    public void A_message_without_a_delay_should_be_stored_and_read_without_a_due_time()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), SystemInfo());

        var entity = OutboxMessage.Map(envelope, Serializer);

        Assert.Null(entity.DeliverAt);
        Assert.True(entity.TryMap(TypeResolver(), Serializer, out var read));
        Assert.Null(read.DeliverAt);
    }

    [Fact]
    public void A_delayed_message_should_keep_its_due_time_when_stored_and_read()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), SystemInfo(), TimeSpan.FromMinutes(10));

        var entity = OutboxMessage.Map(envelope, Serializer);

        Assert.Equal(envelope.DeliverAt, entity.DeliverAt);
        Assert.True(entity.TryMap(TypeResolver(), Serializer, out var read));
        Assert.Equal(envelope.DeliverAt, read.DeliverAt);
    }

    private static ISystemInfo SystemInfo()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.Id.Returns("sender");
        return systemInfo;
    }

    private static TypeResolver TypeResolver()
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));
        return typeResolver;
    }
}
