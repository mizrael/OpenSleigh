using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KafkaMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_should_process_every_message_in_its_own_scope()
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var expectedMessage = DummyMessage.CreateEnvelope();

        var parser = NSubstitute.Substitute.For<IKafkaMessageParser>();
        parser.Parse(consumeResult).Returns(expectedMessage);

        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Create(Arg.Any<MessageEnvelope>()).Returns(new QueueReferences("lorem", "ipsum"));

        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        var scopeFactory = ScopeFactoryFor(messageProcessor, out var scope);

        var sut = new KafkaMessageHandler(
            parser,
            scopeFactory,
            NSubstitute.Substitute.For<IKafkaPublisherExecutor>(),
            NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>(),
            NSubstitute.Substitute.For<ISystemInfo>(),
            queueRefFactory,
            Configuration);

        Assert.True(await sut.HandleAsync(consumeResult));
        Assert.True(await sut.HandleAsync(consumeResult));

        scopeFactory.Received(2).CreateScope();
        scope.Received(2).Dispose();
        await messageProcessor.Received(2).ProcessAsync(expectedMessage, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UseKafkaTransport_should_register_a_handler_the_container_can_build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenSleigh(configurator => configurator.UseKafkaTransport(new KafkaConfiguration("localhost:9092")));
        services.AddTransient(_ => NSubstitute.Substitute.For<IMessageProcessor>());
        services.AddSingleton(NSubstitute.Substitute.For<IKafkaPublisherExecutor>());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<KafkaMessageHandler>(provider.GetRequiredService<IKafkaMessageHandler>());
    }

    [Fact]
    public void Handler_should_have_one_public_constructor_so_that_it_can_be_registered_by_type()
    {
        Assert.Single(typeof(KafkaMessageHandler).GetConstructors());
    }

    private static IServiceScopeFactory ScopeFactoryFor(IMessageProcessor messageProcessor)
        => ScopeFactoryFor(messageProcessor, out _);

    private static IServiceScopeFactory ScopeFactoryFor(IMessageProcessor messageProcessor, out IServiceScope scope)
    {
        var serviceProvider = NSubstitute.Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IMessageProcessor)).Returns(messageProcessor);
        scope = NSubstitute.Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        return scopeFactory;
    }

    [Fact]
    public async Task StartAsync_should_skip_a_message_that_cannot_be_parsed()
    {
        var parser = NSubstitute.Substitute.For<IKafkaMessageParser>();
        parser.WhenForAnyArgs(p => p.Parse(Arg.Any<ConsumeResult<string, byte[]>>()))
              .Throw(new Exception("argh"));

        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();

        var consumeResult = new ConsumeResult<string, byte[]>();
        var queueRefs = new QueueReferences("lorem", "ipsum");

        var sut = new KafkaMessageHandler(parser, ScopeFactoryFor(messageProcessor), publisher, logger, sysInfo, queueRefFactory, Configuration);

        var result = await sut.HandleAsync(consumeResult);
        // delivered again it could not be read either: it is dealt with, by being skipped
        Assert.True(result);

        parser.Received().Parse(consumeResult);

        await messageProcessor.DidNotReceiveWithAnyArgs()
                            .ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_should_process_incoming_messages()
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var expectedMessage = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "ipsum");

        var parser = NSubstitute.Substitute.For<IKafkaMessageParser>();
        parser.Parse(consumeResult)
            .Returns(expectedMessage);

        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Create(Arg.Any<MessageEnvelope>())
            .Returns(queueRefs);

        var sut = new KafkaMessageHandler(parser, ScopeFactoryFor(messageProcessor), publisher, logger, sysInfo, queueRefFactory, Configuration);

        var result = await sut.HandleAsync(consumeResult);
        Assert.True(result);

        await messageProcessor.Received().ProcessAsync((dynamic)expectedMessage, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_should_republish_to_deadletter_when_exception_occurs()
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var expectedMessage = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "ipsum");

        var parser = NSubstitute.Substitute.For<IKafkaMessageParser>();
        parser.Parse(consumeResult)
            .Returns(expectedMessage);

        var ex = new Exception("whoops");
        var expectedErrorHeader = new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message));
        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        messageProcessor.WhenForAnyArgs(mp => ((ValueTask)mp.ProcessAsync((dynamic)expectedMessage)).AsTask())
            .Throw(ex);

        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();

        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Create(Arg.Any<MessageEnvelope>())
           .Returns(queueRefs);

        var sut = new KafkaMessageHandler(parser, ScopeFactoryFor(messageProcessor), publisher, logger, sysInfo, queueRefFactory, Configuration);

        await sut.HandleAsync(consumeResult);

        await publisher.Received(1)
            .PublishAsync(
            expectedMessage,
            queueRefs.DeadLetterTopicName,
            Arg.Is((IEnumerable<Header> headers) => headers.Any(h => h.Key == expectedErrorHeader.Key)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_should_not_republish_to_deadletter_when_exception_occurs_and_no_deadletter_available()
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var expectedMessage = DummyMessage.CreateEnvelope();
        var queueRefs = new QueueReferences("lorem", "");

        var parser = NSubstitute.Substitute.For<IKafkaMessageParser>();
        parser.Parse(consumeResult)
              .Returns(expectedMessage);

        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        messageProcessor.WhenForAnyArgs(mp => ((ValueTask)mp.ProcessAsync((dynamic)expectedMessage)).AsTask())
            .Throw(new Exception("whoops"));

        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        var logger = NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>();

        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();

        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Create(Arg.Any<MessageEnvelope>())
                       .Returns(queueRefs);

        var sut = new KafkaMessageHandler(parser, ScopeFactoryFor(messageProcessor), publisher, logger, sysInfo, queueRefFactory, Configuration);

        await sut.HandleAsync(consumeResult);

        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<MessageEnvelope>(),
            Arg.Any<string>(),
            null,
            Arg.Any<CancellationToken>());
    }

    public static TheoryData<Exception> LockConflicts => new()
    {
        new LockException("locked by someone else"),
        new OptimisticLockException(),
        new AggregateException(new OptimisticLockException(), new LockException("locked by someone else"))
    };

    [Theory]
    [MemberData(nameof(LockConflicts))]
    public async Task HandleAsync_should_publish_to_the_retry_topic_and_not_to_the_dead_letter_one_on_a_lock_conflict(Exception lockConflict)
    {
        var queueRefs = new QueueReferences("lorem", "lorem.dead");
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        IEnumerable<Header>? retryHeaders = null;
        publisher.PublishAsync(Arg.Any<MessageEnvelope>(), queueRefs.RetryTopicName, Arg.Do<IEnumerable<Header>>(headers => retryHeaders = headers), Arg.Any<CancellationToken>())
                 .Returns(new DeliveryResult<string, byte[]>());
        var (sut, message, consumeResult) = CreateSutFailingWith(lockConflict, queueRefs, publisher);

        var before = DateTimeOffset.UtcNow;
        Assert.True(await sut.HandleAsync(consumeResult));

        await publisher.Received(1).PublishAsync(message, "lorem.retry", Arg.Any<IEnumerable<Header>>(), CancellationToken.None);
        await publisher.DidNotReceive().PublishAsync(Arg.Any<MessageEnvelope>(), queueRefs.DeadLetterTopicName, Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>());

        var headers = new Headers();
        foreach (var header in retryHeaders!)
            headers.Add(header);
        Assert.True(RetrySchedule.TryGetNotBefore(headers, out var notBefore));
        Assert.InRange(notBefore, before + Configuration.RetryDelay - TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow + Configuration.RetryDelay);
        Assert.Equal(ClientGroup, Encoding.UTF8.GetString(headers.GetLastBytes(HeaderNames.RetryGroup)));
    }

    [Fact]
    public async Task HandleAsync_should_report_a_message_as_not_dealt_with_when_it_cannot_be_published_anywhere()
    {
        var queueRefs = new QueueReferences("lorem", "lorem.dead");
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        publisher.PublishAsync(Arg.Any<MessageEnvelope>(), Arg.Any<string>(), Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>())
                 .Returns<ValueTask<DeliveryResult<string, byte[]>>>(_ => throw new KafkaException(new Error(ErrorCode.Local_MsgTimedOut, "timed out")));
        var (sut, message, consumeResult) = CreateSutFailingWith(new LockException("locked by someone else"), queueRefs, publisher);

        Assert.False(await sut.HandleAsync(consumeResult));

        await publisher.Received(1).PublishAsync(message, queueRefs.RetryTopicName, Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishAsync(message, queueRefs.DeadLetterTopicName, Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_should_leave_a_message_to_be_delivered_again_when_its_processing_is_interrupted_by_a_stop()
    {
        var queueRefs = new QueueReferences("lorem", "lorem.dead");
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var (sut, _, consumeResult) = CreateSutFailingWith(new OperationCanceledException(stopping.Token), queueRefs, publisher);

        Assert.False(await sut.HandleAsync(consumeResult, stopping.Token));

        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task HandleAsync_should_not_fall_back_to_the_dead_letter_topic_when_the_retry_cannot_be_published_during_a_stop()
    {
        var queueRefs = new QueueReferences("lorem", "lorem.dead");
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        publisher.PublishAsync(Arg.Any<MessageEnvelope>(), queueRefs.RetryTopicName, Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>())
                 .Returns<ValueTask<DeliveryResult<string, byte[]>>>(_ => throw new KafkaException(new Error(ErrorCode.Local_MsgTimedOut, "timed out")));
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var (sut, _, consumeResult) = CreateSutFailingWith(new LockException("locked by someone else"), queueRefs, publisher);

        Assert.False(await sut.HandleAsync(consumeResult, stopping.Token));

        await publisher.DidNotReceive().PublishAsync(Arg.Any<MessageEnvelope>(), queueRefs.DeadLetterTopicName, Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_should_dead_letter_a_message_whose_processing_was_cancelled_by_something_else_than_a_stop()
    {
        var queueRefs = new QueueReferences("lorem", "lorem.dead");
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        publisher.PublishAsync(Arg.Any<MessageEnvelope>(), Arg.Any<string>(), Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>())
                 .Returns(new DeliveryResult<string, byte[]>());
        var (sut, message, consumeResult) = CreateSutFailingWith(new TaskCanceledException("the database timed out"), queueRefs, publisher);

        Assert.True(await sut.HandleAsync(consumeResult));

        await publisher.Received(1).PublishAsync(message, queueRefs.DeadLetterTopicName, Arg.Any<IEnumerable<Header>>(), CancellationToken.None);
    }

    [Fact]
    public async Task HandleAsync_should_publish_to_the_dead_letter_topic_when_the_retry_topic_cannot_be_written()
    {
        var queueRefs = new QueueReferences("lorem", "lorem.dead");
        var publisher = NSubstitute.Substitute.For<IKafkaPublisherExecutor>();
        publisher.PublishAsync(Arg.Any<MessageEnvelope>(), queueRefs.RetryTopicName, Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>())
                 .Returns<ValueTask<DeliveryResult<string, byte[]>>>(_ => throw new KafkaException(new Error(ErrorCode.Local_MsgTimedOut, "timed out")));
        var (sut, message, consumeResult) = CreateSutFailingWith(new LockException("locked by someone else"), queueRefs, publisher);

        Assert.True(await sut.HandleAsync(consumeResult));

        await publisher.Received(1).PublishAsync(message, queueRefs.DeadLetterTopicName, Arg.Any<IEnumerable<Header>>(), Arg.Any<CancellationToken>());
    }

    private static readonly KafkaConfiguration Configuration = new("lorem");

    private static (KafkaMessageHandler sut, MessageEnvelope message, ConsumeResult<string, byte[]> consumeResult) CreateSutFailingWith(
        Exception failure, QueueReferences queueRefs, IKafkaPublisherExecutor publisher)
    {
        var consumeResult = new ConsumeResult<string, byte[]>();
        var message = DummyMessage.CreateEnvelope();

        var parser = NSubstitute.Substitute.For<IKafkaMessageParser>();
        parser.Parse(consumeResult).Returns(message);

        var messageProcessor = NSubstitute.Substitute.For<IMessageProcessor>();
        messageProcessor.ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
                        .Returns(_ => ValueTask.FromException(failure));

        var queueRefFactory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Create(Arg.Any<MessageEnvelope>()).Returns(queueRefs);

        var systemInfo = NSubstitute.Substitute.For<ISystemInfo>();
        systemInfo.ClientGroup.Returns(ClientGroup);

        var sut = new KafkaMessageHandler(
            parser,
            ScopeFactoryFor(messageProcessor),
            publisher,
            NSubstitute.Substitute.For<ILogger<KafkaMessageHandler>>(),
            systemInfo,
            queueRefFactory,
            Configuration);
        return (sut, message, consumeResult);
    }

    private const string ClientGroup = "mine";
}