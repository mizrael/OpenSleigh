using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Transport;

public class DefaultMessageBusTests
{
    [Fact]
    public async Task PublishAsync_should_throw_when_message_is_null()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var systemInfo = Substitute.For<ISystemInfo>();
        var typeResolver = Substitute.For<ITypeResolver>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();
        var sut = new DefaultMessageBus(outboxRepository, systemInfo, typeResolver, logger);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync<FakeSagaStarter>(null!, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_should_register_message_type()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var systemInfo = Substitute.For<ISystemInfo>();
        var typeResolver = Substitute.For<ITypeResolver>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();
        var sut = new DefaultMessageBus(outboxRepository, systemInfo, typeResolver, logger);
       
        var message = new FakeSagaStarter();
        
        await sut.PublishAsync(message, CancellationToken.None);
        
        typeResolver.Received(1).Register(message.GetType());
    }

    [Fact]
    public async Task PublishAsync_should_append_message_to_outbox()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var systemInfo = Substitute.For<ISystemInfo>();
        var typeResolver = Substitute.For<ITypeResolver>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();
        var sut = new DefaultMessageBus(outboxRepository, systemInfo, typeResolver, logger);
        
        var message = new FakeSagaStarter();
        
        await sut.PublishAsync(message, CancellationToken.None);
        
        await outboxRepository.Received(1).AppendAsync(Arg.Any<IEnumerable<MessageEnvelope>>(), CancellationToken.None);
    }
}
