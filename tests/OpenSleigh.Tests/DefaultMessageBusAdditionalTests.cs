using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests;

public class DefaultMessageBusAdditionalTests
{
    [Fact]
    public async Task PublishAsync_should_log_warning_for_duplicate_message()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var systemInfo = Substitute.For<ISystemInfo>();
        var typeResolver = Substitute.For<ITypeResolver>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();

        outboxRepository.AppendAsync(Arg.Any<IEnumerable<MessageEnvelope>>(), Arg.Any<CancellationToken>())
            .Returns(OutboxAppendResult.Duplicate);

        var sut = new DefaultMessageBus(outboxRepository, systemInfo, typeResolver, logger);

        var result = await sut.PublishAsync(new FakeSagaStarter(), CancellationToken.None);

        Assert.Equal(OutboxAppendResult.Duplicate, result);
    }

    [Fact]
    public async Task PublishAsync_should_return_success_for_new_message()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var systemInfo = Substitute.For<ISystemInfo>();
        var typeResolver = Substitute.For<ITypeResolver>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();

        outboxRepository.AppendAsync(Arg.Any<IEnumerable<MessageEnvelope>>(), Arg.Any<CancellationToken>())
            .Returns(OutboxAppendResult.Success);

        var sut = new DefaultMessageBus(outboxRepository, systemInfo, typeResolver, logger);

        var result = await sut.PublishAsync(new FakeSagaStarter(), CancellationToken.None);

        Assert.Equal(OutboxAppendResult.Success, result);
    }

    [Fact]
    public void Ctor_should_throw_when_outboxRepository_is_null()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        var typeResolver = Substitute.For<ITypeResolver>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();

        Assert.Throws<ArgumentNullException>(() => new DefaultMessageBus(null!, systemInfo, typeResolver, logger));
    }

    [Fact]
    public void Ctor_should_throw_when_systemInfo_is_null()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var typeResolver = Substitute.For<ITypeResolver>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();

        Assert.Throws<ArgumentNullException>(() => new DefaultMessageBus(outboxRepository, null!, typeResolver, logger));
    }

    [Fact]
    public void Ctor_should_throw_when_typeResolver_is_null()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var systemInfo = Substitute.For<ISystemInfo>();
        var logger = Substitute.For<ILogger<DefaultMessageBus>>();

        Assert.Throws<ArgumentNullException>(() => new DefaultMessageBus(outboxRepository, systemInfo, null!, logger));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var systemInfo = Substitute.For<ISystemInfo>();
        var typeResolver = Substitute.For<ITypeResolver>();

        Assert.Throws<ArgumentNullException>(() => new DefaultMessageBus(outboxRepository, systemInfo, typeResolver, null!));
    }
}
