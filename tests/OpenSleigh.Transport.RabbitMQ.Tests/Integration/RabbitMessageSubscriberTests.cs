using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using OpenSleigh.Utils;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public class RabbitMessageSubscriberTests : IClassFixture<RabbitFixture>
{
    private readonly RabbitFixture _fixture;

    public RabbitMessageSubscriberTests(RabbitFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartAsync_should_consume_messages()
    {
        var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool received = false;

        var (publisher, sut) = CreateSUT(_ =>
        {
            received = true;

            tokenSource.Cancel();
        });

        await sut.StartAsync();

        var message = CreateMessage();
        await publisher.PublishAsync(message);

        while (!tokenSource.IsCancellationRequested)
            await Task.Delay(10);
        Assert.True(received, "Message was not received");
    }

    [Fact]
    public async Task StartAsync_should_retry_message_when_locked()
    {
        var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var processCount = 0;

        var (publisher, sut) = CreateSUT(_ =>
        {
            processCount++;
            if (1 == processCount)
                throw new LockException("whoops");

            tokenSource.Cancel();
        });

        await sut.StartAsync();

        var message = CreateMessage();
        await publisher.PublishAsync(message);

        while (!tokenSource.IsCancellationRequested)
            await Task.Delay(10);
        Assert.Equal(2, processCount);
    }

    [Fact]
    public async Task StartAsync_should_retry_message_when_AggregateException_with_lock()
    {
        var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var processCount = 0;

        var (publisher, sut) = CreateSUT(_ =>
        {
            processCount++;
            if (1 == processCount)
                throw new AggregateException(new LockException("whoops"));

            tokenSource.Cancel();
        });

        await sut.StartAsync();

        var message = CreateMessage();
        await publisher.PublishAsync(message);

        while (!tokenSource.IsCancellationRequested)
            await Task.Delay(10);
        Assert.Equal(2, processCount);
    }

    private (IPublisher publisher, IMessageSubscriber sut) CreateSUT(Action<MessageEnvelope>? onMessage = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(cfg =>
        {
            cfg.AddJsonConsole();
        });

        var busConfig = Substitute.For<IBusConfigurator>();
        busConfig.Services.Returns(services);

        QueueReferencesCreator queueReferencesCreator = messageType =>
        {
#if NET9_0_OR_GREATER
            var exchangeName = $"{messageType.Name.ToLower()}-{Guid.CreateVersion7().ToString("N")}";
#else
            var exchangeName = $"{messageType.Name.ToLower()}-{Guid.NewGuid().ToString("N")}";
#endif
            var queueName = $"{exchangeName}.workers";
            var dlExchangeName = exchangeName + ".dead";
            var dlQueueName = $"{dlExchangeName}.workers";
            return new QueueReferences(exchangeName, queueName, exchangeName, dlExchangeName, dlQueueName);
        };
        busConfig.UseRabbitMQTransport(_fixture.RabbitConfiguration, queueReferencesCreator);

        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
#if NET9_0_OR_GREATER
        sysInfo.ClientId.Returns(Guid.CreateVersion7().ToString("N"));
        sysInfo.Id.Returns(Guid.CreateVersion7().ToString("N"));
#else
        sysInfo.ClientId.Returns(Guid.NewGuid().ToString("N"));
        sysInfo.Id.Returns(Guid.NewGuid().ToString("N"));
#endif
        services.AddSingleton(sysInfo);

        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(typeof(FakeSagaStarter).FullName)
                    .Returns(typeof(FakeSagaStarter));
        services.AddSingleton(typeResolver);

        services.AddSingleton<ISerializer>(new JsonSerializer());

        var processor = Substitute.For<IMessageProcessor>();
        if(onMessage is not null)
            processor.When(p => p.ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    var message = call.Arg<MessageEnvelope>();
                    onMessage(message);
                });
        services.AddSingleton(processor);

        var sp = services.BuildServiceProvider();

        var queueRefFactory = sp.GetRequiredService<IQueueReferenceFactory>();
        queueRefFactory.Create<FakeSagaStarter>();

        var sut = sp.GetRequiredService<IMessageSubscriber>();
        var publisher = sp.GetRequiredService<IPublisher>();
        return (publisher, sut);
    }

    private static MessageEnvelope CreateMessage()
    {
        var sagaContext = Substitute.For<ISagaInstance >();
        sagaContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        sagaContext.TriggerMessageId.Returns(Guid.NewGuid().ToString());
        sagaContext.InstanceId.Returns(Guid.NewGuid().ToString());
        var serializer = new JsonSerializer();
        var message = MessageEnvelope.Create(new FakeSagaStarter(), sagaContext);
        return message;
    }
}
