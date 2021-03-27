using System;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public class ServiceBusSenderFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_argument_null()
        {
            var serviceBusClient = NSubstitute.Substitute.ForPartsOf<ServiceBusClient>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSenderFactory(null, serviceBusClient));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSenderFactory(factory, null));
        }

        [Fact]
        public void Create_should_return_sender()
        {
            var serviceBusClient = NSubstitute.Substitute.ForPartsOf<ServiceBusClient>();
            var sender = NSubstitute.Substitute.ForPartsOf<ServiceBusSender>();
            serviceBusClient.WhenForAnyArgs(c => c.CreateSender(Arg.Any<string>()))
                .DoNotCallBase();
            serviceBusClient.CreateSender(Arg.Any<string>()).ReturnsForAnyArgs(sender);
                
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var references = new QueueReferences("lorem", "ipsum");
            factory.Create<DummyMessage>()
                .Returns(references);
            
            var sut = new ServiceBusSenderFactory(factory, serviceBusClient);
            var result = sut.Create<DummyMessage>();
            result.Should().Be(sender);

            serviceBusClient.Received(1)
                .CreateSender(references.TopicName);
        }

        [Fact]
        public void Create_should_recreate_sender_when_null()
        {
            var serviceBusClient = NSubstitute.Substitute.ForPartsOf<ServiceBusClient>();
            
            serviceBusClient.WhenForAnyArgs(c => c.CreateSender(Arg.Any<string>()))
                .DoNotCallBase();
            serviceBusClient.CreateSender(Arg.Any<string>()).ReturnsNullForAnyArgs();

            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var references = new QueueReferences("lorem", "ipsum");
            factory.Create<DummyMessage>()
                .Returns(references);

            var sut = new ServiceBusSenderFactory(factory, serviceBusClient);
            sut.Create<DummyMessage>();

            serviceBusClient.Received(2)
                .CreateSender(references.TopicName);
        }
    }
}
