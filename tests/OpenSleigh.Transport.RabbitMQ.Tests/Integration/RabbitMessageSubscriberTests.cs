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

    [Fact]
    public async Task StartAsync_should_dead_letter_a_message_whose_handler_failed_without_retrying_it()
    {
        var processCount = 0;
        var (publisher, sut) = CreateSUT(_ =>
        {
            Interlocked.Increment(ref processCount);
            throw new SagaException("the handler failed", new InvalidOperationException("whoops"));
        }, out var services);

        await sut.StartAsync();

        var message = CreateMessage();
        await publisher.PublishAsync(message);

        var deadLetterQueue = services.GetRequiredService<IQueueReferenceFactory>().Create(message).DeadLetterQueue;
        var channel = await services.GetRequiredService<IChannelFactory>().GetPublishChannelAsync();
        global::RabbitMQ.Client.BasicGetResult? deadLetter = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (deadLetter is null && DateTime.UtcNow < deadline)
        {
            deadLetter = await channel.BasicGetAsync(deadLetterQueue, autoAck: true);
            if (deadLetter is null)
                await Task.Delay(50);
        }

        Assert.NotNull(deadLetter);
        Assert.Equal(1, processCount);
    }

    [Fact]
    public async Task StartAsync_should_retry_a_locked_message_only_in_the_service_that_could_not_process_it()
    {
        var exchangeName = NewExchangeName();
        var conflictingServiceCount = 0;
        var otherServiceCount = 0;
        var processedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (publisher, conflictingService) = CreateSUT(_ =>
        {
            if (Interlocked.Increment(ref conflictingServiceCount) == 1)
                throw new LockException("whoops");

            processedAgain.TrySetResult();
        }, out _, exchangeName, clientGroup: "orders");
        var (_, otherService) = CreateSUT(_ => Interlocked.Increment(ref otherServiceCount), out _, exchangeName, clientGroup: "billing");

        await conflictingService.StartAsync();
        await otherService.StartAsync();
        await publisher.PublishAsync(CreateMessage());

        await processedAgain.Task.WaitAsync(TimeSpan.FromSeconds(20));
        // time for a copy of the retried message to reach the other service, and to come back once more
        await Task.Delay(_fixture.RabbitConfiguration.RetryDelay * 3);

        Assert.Equal(2, conflictingServiceCount);
        Assert.Equal(1, otherServiceCount);
    }

    [Fact]
    public async Task StartAsync_should_work_on_the_topology_of_an_earlier_version_and_deliver_what_waits_in_its_retry_queue()
    {
        var exchangeName = NewExchangeName();
        var references = DefaultReferences(exchangeName, "test");
        var processCount = 0;
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (publisher, sut) = CreateSUT(_ =>
        {
            Interlocked.Increment(ref processCount);
            processed.TrySetResult();
        }, out _, exchangeName);

        await using var connection = await _fixture.ConnectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await DeclareRetryTopologyOfVersion330Async(channel, references);

        // the message is taken out of its queue and put in the old retry queue, as a retry of that version would have
        await publisher.PublishAsync(CreateMessage());
        var waiting = await GetAsync(channel, references.QueueName);
        Assert.NotNull(waiting);
        await channel.BasicPublishAsync(
            exchange: references.RetryExchangeName,
            routingKey: references.RoutingKey,
            mandatory: true,
            basicProperties: new global::RabbitMQ.Client.BasicProperties(waiting.BasicProperties),
            body: waiting.Body);

        await sut.StartAsync();
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(_fixture.RabbitConfiguration.RetryDelay * 2);

        Assert.Equal(1, processCount);
    }

    [Fact]
    public async Task StartAsync_should_not_dead_letter_a_message_whose_processing_was_interrupted_by_a_stop()
    {
        using var stopping = new CancellationTokenSource();
        var processCount = 0;
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (publisher, sut) = CreateSUT(_ =>
        {
            Interlocked.Increment(ref processCount);
            stopping.Cancel();
            interrupted.TrySetResult();
            throw new OperationCanceledException(stopping.Token);
        }, out var services);

        await sut.StartAsync(stopping.Token);
        var message = CreateMessage();
        await publisher.PublishAsync(message);
        await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(500);

        // neither rejected, which would dead-letter it, nor given back to this consumer over and over:
        // it stays unacknowledged until the channel closes
        var deadLetterQueue = services.GetRequiredService<IQueueReferenceFactory>().Create(message).DeadLetterQueue;
        var channel = await services.GetRequiredService<IChannelFactory>().GetPublishChannelAsync();
        Assert.Null(await channel.BasicGetAsync(deadLetterQueue, autoAck: true));
        Assert.Equal(1, processCount);
    }

    [Fact]
    public async Task A_host_that_publishes_a_message_it_does_not_handle_should_declare_no_queue_of_its_own()
    {
        var exchangeName = NewExchangeName();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (_, worker) = CreateSUT(_ => received.TrySetResult(), out _, exchangeName, clientGroup: "workers");
        var (publisher, _) = CreateSUT(null, out _, exchangeName, clientGroup: "api", handlesTheMessage: false);

        await worker.StartAsync();
        await publisher.PublishAsync(CreateMessage());
        await received.Task.WaitAsync(TimeSpan.FromSeconds(20));

        await using var connection = await _fixture.ConnectionFactory.CreateConnectionAsync();
        var ofThePublisher = DefaultReferences(exchangeName, "api");
        Assert.False(await QueueExistsAsync(connection, ofThePublisher.QueueName));
        Assert.False(await QueueExistsAsync(connection, ofThePublisher.RetryQueueName));
        Assert.False(await QueueExistsAsync(connection, ofThePublisher.DeadLetterQueue));

        // the service that consumes the message still has all of its own
        var ofTheWorker = DefaultReferences(exchangeName, "workers");
        Assert.True(await QueueExistsAsync(connection, ofTheWorker.QueueName));
        Assert.True(await QueueExistsAsync(connection, ofTheWorker.RetryQueueName));
        Assert.True(await QueueExistsAsync(connection, ofTheWorker.DeadLetterQueue));
    }

    // a channel is closed by the broker when it asks for a queue that does not exist: one per question
    private static async Task<bool> QueueExistsAsync(global::RabbitMQ.Client.IConnection connection, string queueName)
    {
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            await channel.QueueDeclarePassiveAsync(queueName);
            return true;
        }
        catch (global::RabbitMQ.Client.Exceptions.OperationInterruptedException e)
            when (e.ShutdownReason?.ReplyCode == global::RabbitMQ.Client.Constants.NotFound)
        {
            return false;
        }
    }

    private async Task DeclareRetryTopologyOfVersion330Async(global::RabbitMQ.Client.IChannel channel, QueueReferences references)
    {
        var configuration = _fixture.RabbitConfiguration;
        await channel.ExchangeDeclareAsync(references.ExchangeName, global::RabbitMQ.Client.ExchangeType.Topic, configuration.Durable, configuration.AutoDelete);
        await channel.ExchangeDeclareAsync(references.RetryExchangeName, global::RabbitMQ.Client.ExchangeType.Topic, configuration.Durable, configuration.AutoDelete);
        await channel.QueueDeclareAsync(references.LegacyRetryQueueName, configuration.Durable, exclusive: false, configuration.AutoDelete,
            arguments: new System.Collections.Generic.Dictionary<string, object?>
            {
                { global::RabbitMQ.Client.Headers.XMessageTTL, (int)configuration.RetryDelay.TotalMilliseconds },
                { global::RabbitMQ.Client.Headers.XDeadLetterExchange, references.ExchangeName },
                { global::RabbitMQ.Client.Headers.XDeadLetterRoutingKey, references.RoutingKey }
            });
        await channel.QueueBindAsync(references.LegacyRetryQueueName, references.RetryExchangeName, references.RoutingKey);
    }

    private static async Task<global::RabbitMQ.Client.BasicGetResult?> GetAsync(global::RabbitMQ.Client.IChannel channel, string queueName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queueName, autoAck: true);
            if (result is not null)
                return result;

            await Task.Delay(50);
        }

        return null;
    }

    private (IPublisher publisher, IMessageSubscriber sut) CreateSUT(Action<MessageEnvelope>? onMessage = null)
        => CreateSUT(onMessage, out _);

    private (IPublisher publisher, IMessageSubscriber sut) CreateSUT(
        Action<MessageEnvelope>? onMessage,
        out IServiceProvider provider,
        string? exchangeName = null,
        string clientGroup = "test",
        bool handlesTheMessage = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(cfg =>
        {
            cfg.AddJsonConsole();
        });

        var busConfig = Substitute.For<IBusConfigurator>();
        busConfig.Services.Returns(services);

        exchangeName ??= NewExchangeName();
        busConfig.UseRabbitMQTransport(_fixture.RabbitConfiguration, _ => DefaultReferences(exchangeName, clientGroup));

        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns(clientGroup);
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

        var sagaDescriptorResolver = Substitute.For<ISagaDescriptorsResolver>();
        sagaDescriptorResolver.GetRegisteredMessageTypes()
            .Returns(handlesTheMessage ? new[] { typeof(FakeSagaStarter) } : Array.Empty<Type>());
        services.AddSingleton<ISagaDescriptorsResolver>(sagaDescriptorResolver);

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
        provider = sp;

        var sut = sp.GetRequiredService<IMessageSubscriber>();
        var publisher = sp.GetRequiredService<IPublisher>();
        return (publisher, sut);
    }

    private static string NewExchangeName()
#if NET9_0_OR_GREATER
        => $"{nameof(FakeSagaStarter).ToLower()}-{Guid.CreateVersion7():N}";
#else
        => $"{nameof(FakeSagaStarter).ToLower()}-{Guid.NewGuid():N}";
#endif

    // the names the default naming gives: one exchange and one routing key for the message type, one queue per client group
    private static QueueReferences DefaultReferences(string exchangeName, string clientGroup)
    {
        var dlExchangeName = exchangeName + ".dead";
        return new QueueReferences(
            exchangeName,
            $"{exchangeName}.{clientGroup}.workers",
            exchangeName,
            dlExchangeName,
            $"{dlExchangeName}.{clientGroup}.workers");
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
