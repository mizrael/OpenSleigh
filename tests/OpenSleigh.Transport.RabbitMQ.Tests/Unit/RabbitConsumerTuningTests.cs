using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.DependencyInjection;
using RabbitMQ.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

public class RabbitConsumerTuningTests
{
    [Fact]
    public void ConsumerDispatchConcurrency_should_default_to_one()
    {
        Assert.Equal(1, CreateConfiguration().ConsumerDispatchConcurrency);
    }

    [Fact]
    public void ConsumerDispatchConcurrency_should_reject_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateConfiguration() with { ConsumerDispatchConcurrency = 0 });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void UseRabbitMQTransport_should_pass_the_dispatch_concurrency_to_the_connection_factory(ushort concurrency)
    {
        var configuration = CreateConfiguration() with { ConsumerDispatchConcurrency = concurrency };
        using var provider = new ServiceCollection()
            .AddOpenSleigh(configurator => configurator.UseRabbitMQTransport(configuration))
            .BuildServiceProvider();

        var connectionFactory = Assert.IsType<ConnectionFactory>(provider.GetRequiredService<IConnectionFactory>());

        Assert.Equal(concurrency, connectionFactory.ConsumerDispatchConcurrency);
    }

    [Fact]
    public async Task StartAsync_should_not_set_a_prefetch_limit_by_default()
    {
        var channel = Substitute.For<IChannel>();
        var sut = CreateSubscriber(channel, CreateConfiguration());

        await sut.StartAsync();

        await channel.DidNotReceiveWithAnyArgs().BasicQosAsync(default, default, default, default);
    }

    [Fact]
    public async Task StartAsync_should_set_the_configured_prefetch_limit_on_the_consumer_channel()
    {
        var channel = Substitute.For<IChannel>();
        var sut = CreateSubscriber(channel, CreateConfiguration() with { PrefetchCount = 64 });

        await sut.StartAsync();

        await channel.Received(1).BasicQosAsync(0, 64, false, Arg.Any<CancellationToken>());
    }

    private static RabbitConfiguration CreateConfiguration() => new("localhost", "guest", "guest");

    private static RabbitMessageSubscriber CreateSubscriber(IChannel channel, RabbitConfiguration configuration)
    {
        var channelFactory = Substitute.For<IChannelFactory>();
        channelFactory.GetConsumeChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var descriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        descriptorsResolver.GetRegisteredMessageTypes().Returns(Array.Empty<Type>());

        return new RabbitMessageSubscriber(
            channelFactory,
            Substitute.For<IQueueReferenceFactory>(),
            configuration,
            Substitute.For<IServiceProvider>(),
            new RabbitMessageParser(Substitute.For<OpenSleigh.Utils.ITypeResolver>(), Substitute.For<OpenSleigh.Utils.ISerializer>()),
            descriptorsResolver,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RabbitMessageSubscriber>.Instance);
    }
}
