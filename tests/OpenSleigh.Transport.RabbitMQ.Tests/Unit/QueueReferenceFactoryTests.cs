using NSubstitute;
using OpenSleigh.Outbox;
using System;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

public class QueueReferenceFactoryTests
{
    [Fact]
    public void Create_should_use_provided_creator()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");

        var sut = new QueueReferenceFactory(messageType =>
        {
            var exchangeName = messageType.Name.ToLower();
            var queueName = exchangeName + ".a";
            var routingKey = queueName;
            var dlExchangeName = exchangeName + ".b";
            var dlQueueName = dlExchangeName + ".c";
            return new QueueReferences(exchangeName, queueName, routingKey, dlExchangeName, dlQueueName);
        });

        var message = MessageEnvelope.Create(new FakeSagaStarter(), sysInfo);
        var result = sut.Create(message);
        Assert.NotNull(result);
        Assert.Equal("fakesagastarter", result.ExchangeName);
        Assert.Equal("fakesagastarter.a", result.QueueName);
        Assert.Equal("fakesagastarter.a", result.RoutingKey);
        Assert.Equal("fakesagastarter.b", result.DeadLetterExchangeName);
        Assert.Equal("fakesagastarter.b.c", result.DeadLetterQueue);
        Assert.Equal("fakesagastarter.retry", result.RetryExchangeName);
        Assert.Equal("fakesagastarter.a.retry", result.RetryQueueName);
    }

    [Fact]
    public void BuildDefaultCreator_should_return_valid_instance()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");
        var sut = QueueReferenceFactory.BuildDefaultCreator(sysInfo);
        var result = sut(typeof(FakeSagaStarter));
        Assert.NotNull(result);
        Assert.Equal("fakesagastarter", result.ExchangeName);
        Assert.Equal("fakesagastarter.test.workers", result.QueueName);
        Assert.Equal("fakesagastarter", result.RoutingKey);
        Assert.Equal("fakesagastarter.dead", result.DeadLetterExchangeName);
        Assert.Equal("fakesagastarter.dead.test.workers", result.DeadLetterQueue);
    }

    [Fact]
    public void Create_should_return_valid_references()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");

        var creator = QueueReferenceFactory.BuildDefaultCreator(sysInfo);
        var sut = new QueueReferenceFactory(creator);
        var message = MessageEnvelope.Create(new FakeSagaStarter(), sysInfo);
        var result = sut.Create(message);
        Assert.NotNull(result);
        Assert.Equal("fakesagastarter", result.ExchangeName);
        Assert.Equal("fakesagastarter.test.workers", result.QueueName);
        Assert.Equal("fakesagastarter", result.RoutingKey);
        Assert.Equal("fakesagastarter.dead", result.DeadLetterExchangeName);
        Assert.Equal("fakesagastarter.dead.test.workers", result.DeadLetterQueue);
        Assert.Equal("fakesagastarter.retry", result.RetryExchangeName);
        Assert.Equal("fakesagastarter.test.workers.retry", result.RetryQueueName);
    }

    [Fact]
    public void Create_generic_should_return_valid_references()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");

        var creator = QueueReferenceFactory.BuildDefaultCreator(sysInfo);
        var sut = new QueueReferenceFactory(creator);
        var result = sut.Create<FakeSagaStarter>();
        Assert.NotNull(result);
        Assert.Equal("fakesagastarter", result.ExchangeName);
        Assert.Equal("fakesagastarter.test.workers", result.QueueName);
        Assert.Equal("fakesagastarter", result.RoutingKey);
        Assert.Equal("fakesagastarter.dead", result.DeadLetterExchangeName);
        Assert.Equal("fakesagastarter.dead.test.workers", result.DeadLetterQueue);
        Assert.Equal("fakesagastarter.retry", result.RetryExchangeName);
        Assert.Equal("fakesagastarter.test.workers.retry", result.RetryQueueName);
    }

    [Fact]
    public void ctor_should_throw_if_input_null()
    {
        Assert.Throws<ArgumentNullException>(() => new QueueReferenceFactory(null!));
    }

    [Fact]
    public void Create_by_type_should_throw_if_messageType_is_null()
    {
        var sysInfo = Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");

        var creator = QueueReferenceFactory.BuildDefaultCreator(sysInfo);
        var sut = new QueueReferenceFactory(creator);

        Assert.Throws<ArgumentNullException>(() => sut.Create((Type)null!));
    }

    [Fact]
    public void Create_by_type_should_throw_if_type_does_not_implement_IMessage()
    {
        var sysInfo = Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");

        var creator = QueueReferenceFactory.BuildDefaultCreator(sysInfo);
        var sut = new QueueReferenceFactory(creator);

        var ex = Assert.Throws<ArgumentException>(() => sut.Create(typeof(string)));
        Assert.Contains("does not implement IMessage interface", ex.Message);
        Assert.Equal("messageType", ex.ParamName);
    }

    [Fact]
    public void Create_by_type_should_return_valid_references()
    {
        var sysInfo = Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");

        var creator = QueueReferenceFactory.BuildDefaultCreator(sysInfo);
        var sut = new QueueReferenceFactory(creator);
        var result = sut.Create(typeof(FakeSagaStarter));

        Assert.NotNull(result);
        Assert.Equal("fakesagastarter", result.ExchangeName);
        Assert.Equal("fakesagastarter.test.workers", result.QueueName);
        Assert.Equal("fakesagastarter", result.RoutingKey);
        Assert.Equal("fakesagastarter.dead", result.DeadLetterExchangeName);
        Assert.Equal("fakesagastarter.dead.test.workers", result.DeadLetterQueue);
        Assert.Equal("fakesagastarter.retry", result.RetryExchangeName);
        Assert.Equal("fakesagastarter.test.workers.retry", result.RetryQueueName);
    }

    [Fact]
    public void Create_by_type_should_cache_references()
    {
        var sysInfo = Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");

        var callCount = 0;
        var sut = new QueueReferenceFactory(messageType =>
        {
            callCount++;
            return new QueueReferences("exchange", "queue", "routing", "dlExchange", "dlQueue");
        });

        var result1 = sut.Create(typeof(FakeSagaStarter));
        var result2 = sut.Create(typeof(FakeSagaStarter));

        Assert.Equal(1, callCount);
        Assert.Same(result1, result2);
    }
}
