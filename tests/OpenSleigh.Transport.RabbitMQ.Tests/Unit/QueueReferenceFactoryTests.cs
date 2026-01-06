using FluentAssertions;
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
        result.Should().NotBeNull();
        result.ExchangeName.Should().Be("fakesagastarter");
        result.QueueName.Should().Be("fakesagastarter.a");
        result.RoutingKey.Should().Be("fakesagastarter.a");
        result.DeadLetterExchangeName.Should().Be("fakesagastarter.b");
        result.DeadLetterQueue.Should().Be("fakesagastarter.b.c");
        result.RetryExchangeName.Should().Be("fakesagastarter.retry");
        result.RetryQueueName.Should().Be("fakesagastarter.a.retry");
    }

    [Fact]
    public void BuildDefaultCreator_should_return_valid_instance()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");
        var sut = QueueReferenceFactory.BuildDefaultCreator(sysInfo);
        var result = sut(typeof(FakeSagaStarter));
        result.Should().NotBeNull();
        result.ExchangeName.Should().Be("fakesagastarter");
        result.QueueName.Should().Be("fakesagastarter.test.workers");
        result.RoutingKey.Should().Be("fakesagastarter");
        result.DeadLetterExchangeName.Should().Be("fakesagastarter.dead");
        result.DeadLetterQueue.Should().Be("fakesagastarter.dead.test.workers");
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
        result.Should().NotBeNull();
        result.ExchangeName.Should().Be("fakesagastarter");
        result.QueueName.Should().Be("fakesagastarter.test.workers");
        result.RoutingKey.Should().Be("fakesagastarter");
        result.DeadLetterExchangeName.Should().Be("fakesagastarter.dead");
        result.DeadLetterQueue.Should().Be("fakesagastarter.dead.test.workers");
        result.RetryExchangeName.Should().Be("fakesagastarter.retry");
        result.RetryQueueName.Should().Be("fakesagastarter.test.workers.retry");
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
        result.Should().NotBeNull();
        result.ExchangeName.Should().Be("fakesagastarter");
        result.QueueName.Should().Be("fakesagastarter.test.workers");
        result.RoutingKey.Should().Be("fakesagastarter");
        result.DeadLetterExchangeName.Should().Be("fakesagastarter.dead");
        result.DeadLetterQueue.Should().Be("fakesagastarter.dead.test.workers");
        result.RetryExchangeName.Should().Be("fakesagastarter.retry");
        result.RetryQueueName.Should().Be("fakesagastarter.test.workers.retry");
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
        ex.Message.Should().Contain("does not implement IMessage interface");
        ex.ParamName.Should().Be("messageType");
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

        result.Should().NotBeNull();
        result.ExchangeName.Should().Be("fakesagastarter");
        result.QueueName.Should().Be("fakesagastarter.test.workers");
        result.RoutingKey.Should().Be("fakesagastarter");
        result.DeadLetterExchangeName.Should().Be("fakesagastarter.dead");
        result.DeadLetterQueue.Should().Be("fakesagastarter.dead.test.workers");
        result.RetryExchangeName.Should().Be("fakesagastarter.retry");
        result.RetryQueueName.Should().Be("fakesagastarter.test.workers.retry");
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

        callCount.Should().Be(1);
        result1.Should().BeSameAs(result2);
    }
}
