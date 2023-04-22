using FluentAssertions;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Tests;
using OpenSleigh.Utils;
using System;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class QueueReferenceFactoryTests
    {
        [Fact]
        public void Create_should_use_provided_creator()
        {
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
            sysInfo.ClientGroup.Returns("test");
            sysInfo.ClientId.Returns("client");
            
            var sut = new QueueReferenceFactory(sysInfo, messageType =>
            {
                var exchangeName = messageType.Name.ToLower();
                var queueName = exchangeName + ".a";
                var routingKey = queueName;
                var dlExchangeName = exchangeName + ".b";
                var dlQueueName = dlExchangeName + ".c";
                return new QueueReferences(exchangeName, queueName, routingKey, dlExchangeName, dlQueueName);
            });
            
            var message = OutboxMessage.Create(new FakeSagaStarter(), sysInfo, new JsonSerializer());
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
        public void Create_should_return_valid_references()
        {
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
            sysInfo.ClientGroup.Returns("test");
            sysInfo.ClientId.Returns("client");

            var sut = new QueueReferenceFactory(sysInfo);
            var message = OutboxMessage.Create(new FakeSagaStarter(), sysInfo, new JsonSerializer());
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

            var sut = new QueueReferenceFactory(sysInfo);
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
            Assert.Throws<ArgumentNullException>(() => new QueueReferenceFactory(null));
        }
    }
}
