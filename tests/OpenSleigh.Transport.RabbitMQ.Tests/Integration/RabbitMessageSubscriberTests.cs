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
        var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        bool received = false;

        var (publisher, sut) = CreateSUT(_ =>
        {
            received = true;

            tokenSource.Cancel();
        });

        sut.Start();

        var message = CreateMessage();
        await publisher.PublishAsync(message);

        while (!tokenSource.IsCancellationRequested)
            await Task.Delay(10);
        Assert.True(received, "Message was not received");
    }

    [Fact]
    public async Task StartAsync_should_retry_message_when_locked()
    {
        var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var processCount = 0;

        var (publisher, sut) = CreateSUT(_ =>
        {
            processCount++;
            if (1 == processCount)
                throw new LockException("whoops");

            tokenSource.Cancel();
        });

        sut.Start();

        var message = CreateMessage();
        await publisher.PublishAsync(message);

        while (!tokenSource.IsCancellationRequested)
            await Task.Delay(10);
        Assert.Equal(2, processCount);
    }

    [Fact]
    public async Task StartAsync_should_retry_message_when_AggregateException_with_lock()
    {
        var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var processCount = 0;

        var (publisher, sut) = CreateSUT(_ =>
        {
            processCount++;
            if (1 == processCount)
                throw new AggregateException(new LockException("whoops"));

            tokenSource.Cancel();
        });

        sut.Start();

        var message = CreateMessage();
        await publisher.PublishAsync(message);

        while (!tokenSource.IsCancellationRequested)
            await Task.Delay(10);
        Assert.Equal(2, processCount);
    }

    private (IPublisher publisher, IMessageSubscriber<FakeSagaStarter> sut) CreateSUT(Action<OutboxMessage>? onMessage = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(cfg =>
        {
            cfg.AddJsonConsole();
        });

        var busConfig = Substitute.For<IBusConfigurator>();
        busConfig.Services.Returns(services);
        busConfig.UseRabbitMQTransport(_fixture.RabbitConfiguration);

        QueueReferencesCreator queueReferencesCreator = messageType =>
        {
            var exchangeName = $"{messageType.Name.ToLower()}-{Guid.CreateVersion7().ToString("N")}";
            var queueName = $"{exchangeName}.workers";
            var dlExchangeName = exchangeName + ".dead";
            var dlQueueName = $"{dlExchangeName}.workers";
            return new QueueReferences(exchangeName, queueName, exchangeName, dlExchangeName, dlQueueName);
        };
        services.AddSingleton(queueReferencesCreator);

        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns(Guid.NewGuid().ToString());
        sysInfo.Id.Returns(Guid.NewGuid().ToString());
        services.AddSingleton(sysInfo);

        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(typeof(FakeSagaStarter).FullName)
                    .Returns(typeof(FakeSagaStarter));
        services.AddSingleton(typeResolver);

        var processor = Substitute.For<IMessageProcessor>();
        if(onMessage is not null)
            processor.When(p => p.ProcessAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    var message = call.Arg<OutboxMessage>();
                    onMessage(message);
                });
        services.AddSingleton(processor);

        var sp = services.BuildServiceProvider();

        var sut = sp.GetRequiredService<IMessageSubscriber<FakeSagaStarter>>();
        var publisher = sp.GetRequiredService<IPublisher>();
        return (publisher, sut);
    }

    private static OutboxMessage CreateMessage()
    {
        var sagaContext = Substitute.For<ISagaExecutionContext>();
        sagaContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        sagaContext.TriggerMessageId.Returns(Guid.NewGuid().ToString());
        sagaContext.InstanceId.Returns(Guid.NewGuid().ToString());
        var serializer = new JsonSerializer();
        var message = OutboxMessage.Create(new FakeSagaStarter(), serializer, sagaContext);
        return message;
    }

    //[Fact]
    //public async Task StartAsync_should_retry_message_when_AggregateException_with_lock()
    //{
    //    var message = DummyMessage.New();
    //    var encodedMessage = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(message));

    //    using var connection = _fixture.Connect();
    //    using var channel = connection.CreateModel();
    //    var queueRef = _fixture.CreateQueueReference("test_publisher");

    //    var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    //    var busConn = Substitute.For<IBusConnection>();
    //    busConn.CreateChannel()
    //        .Returns(channel);

    //    var queueRefFactory = Substitute.For<IQueueReferenceFactory>();
    //    queueRefFactory.Create<DummyMessage>()
    //        .ReturnsForAnyArgs(queueRef);

    //    var messageParser = Substitute.For<IMessageParser>();
    //    messageParser.Resolve(null, null)
    //        .ReturnsForAnyArgs(message);

    //    var processCount = 0;
    //    var processor = Substitute.For<IMessageProcessor>();
    //    processor.When(p => p.ProcessAsync(Arg.Any<DummyMessage>(), Arg.Any<CancellationToken>()))
    //        .Do(p =>
    //        {
    //            processCount++;
    //            if (1 == processCount)
    //                throw new AggregateException(new LockException("whoops"));

    //            tokenSource.Cancel();
    //        });

    //    var logger = Substitute.For<ILogger<RabbitSubscriber<DummyMessage>>>();

    //    var sut = new RabbitSubscriber<DummyMessage>(busConn, queueRefFactory, messageParser,
    //                                                processor, logger, _fixture.RabbitConfiguration);

    //    await sut.StartAsync();

    //    var props = channel.CreateBasicProperties();
    //    channel.BasicPublish(queueRef.ExchangeName, queueRef.QueueName, false, props, encodedMessage);

    //    while (!tokenSource.IsCancellationRequested)
    //        await Task.Delay(10);

    //    processCount.Should().BeGreaterThan(0);
    //}
}
