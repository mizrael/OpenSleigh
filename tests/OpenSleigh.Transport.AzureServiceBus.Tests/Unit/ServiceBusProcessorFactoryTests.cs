using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public class ServiceBusProcessorFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_argument_null()
        {
            var serviceBusClient = NSubstitute.Substitute.ForPartsOf<ServiceBusClient>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            Assert.Throws<ArgumentNullException>(() => new ServiceBusProcessorFactory(null, serviceBusClient));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusProcessorFactory(factory, null));
        }

        [Fact]
        public void Create_should_return_Processor()
        {
            var serviceBusClient = NSubstitute.Substitute.ForPartsOf<ServiceBusClient>();
            var processor = NSubstitute.Substitute.ForPartsOf<ServiceBusProcessor>();
            serviceBusClient.WhenForAnyArgs(c => c.CreateProcessor(Arg.Any<string>(), Arg.Any<string>()))
                .DoNotCallBase();
            serviceBusClient.CreateProcessor(Arg.Any<string>(), Arg.Any<string>())
                .ReturnsForAnyArgs(processor);
                
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var references = new QueueReferences("lorem", "ipsum");
            factory.Create<DummyMessage>()
                .Returns(references);
            
            var sut = new ServiceBusProcessorFactory(factory, serviceBusClient);
            var result = sut.Create<DummyMessage>();
            result.Should().Be(processor);

            serviceBusClient.Received(1)
                .CreateProcessor(references.TopicName, references.SubscriptionName);
        }

        [Fact]
        public void Create_should_recreate_Processor_when_null()
        {
            var serviceBusClient = NSubstitute.Substitute.ForPartsOf<ServiceBusClient>();
            
            serviceBusClient.WhenForAnyArgs(c => c.CreateProcessor(Arg.Any<string>(), Arg.Any<string>()))
                .DoNotCallBase();
            serviceBusClient.CreateProcessor(Arg.Any<string>(), Arg.Any<string>())
                .ReturnsNullForAnyArgs();

            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var references = new QueueReferences("lorem", "ipsum");
            factory.Create<DummyMessage>()
                .Returns(references);

            var sut = new ServiceBusProcessorFactory(factory, serviceBusClient);
            sut.Create<DummyMessage>();

            serviceBusClient.Received(2)
                .CreateProcessor(references.TopicName, references.SubscriptionName);
        }
    }
}
