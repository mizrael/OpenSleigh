using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;
using System;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public class ServiceBusSubscriberTests
    {
        [Fact]
        public void ctor_should_throw_when_input_invalid()
        {
            var queueReferenceFactory = Substitute.For<IQueueReferenceFactory>();
            var serviceBusClient = Substitute.For<ServiceBusClient>();
            var messageParser = Substitute.For<ITransportSerializer>();
            var messageProcessor = Substitute.For<IMessageProcessor>();
            var logger = Substitute.For<ILogger<ServiceBusSubscriber<DummyMessage>>>();
            var sbConfig = new AzureServiceBusConfiguration("lorem");
            var systemInfo = Substitute.For<ISystemInfo>();

            Assert.Throws<ArgumentNullException>(() => new ServiceBusSubscriber<DummyMessage>(null, serviceBusClient, messageParser, messageProcessor, logger, sbConfig, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSubscriber<DummyMessage>(queueReferenceFactory, null, messageParser, messageProcessor, logger, sbConfig, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSubscriber<DummyMessage>(queueReferenceFactory, serviceBusClient, null, messageProcessor, logger, sbConfig, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSubscriber<DummyMessage>(queueReferenceFactory, serviceBusClient, messageParser, null, logger, sbConfig, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSubscriber<DummyMessage>(queueReferenceFactory, serviceBusClient, messageParser, messageProcessor, null, sbConfig, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSubscriber<DummyMessage>(queueReferenceFactory, serviceBusClient, messageParser, messageProcessor, logger, null, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusSubscriber<DummyMessage>(queueReferenceFactory, serviceBusClient, messageParser, messageProcessor, logger, sbConfig, null));
        }

        [Fact]
        public void ctor_should_create_servicebus_processor()
        {
            var queueRefs = new QueueReferences("test topic", "test subscription");
            var queueReferenceFactory = Substitute.For<IQueueReferenceFactory>();
            queueReferenceFactory.Create<DummyMessage>().Returns(queueRefs);

            var processor = Substitute.For<ServiceBusProcessor>();

            var serviceBusClient = Substitute.For<ServiceBusClient>();
            serviceBusClient.CreateProcessor(queueRefs.TopicName,
                                     queueRefs.SubscriptionName,
                                     Arg.Any<ServiceBusProcessorOptions>())
                .ReturnsForAnyArgs(processor);

            var messageParser = Substitute.For<ITransportSerializer>();
            var messageProcessor = Substitute.For<IMessageProcessor>();
            var logger = Substitute.For<ILogger<ServiceBusSubscriber<DummyMessage>>>();
            var sbConfig = new AzureServiceBusConfiguration("lorem", 42);
            var systemInfo = Substitute.For<ISystemInfo>();

            var sut = new ServiceBusSubscriber<DummyMessage>(queueReferenceFactory, serviceBusClient, messageParser, messageProcessor, logger, sbConfig, systemInfo);

            serviceBusClient.Received(1)
                    .CreateProcessor(queueRefs.TopicName,
                                     queueRefs.SubscriptionName,
                                     Arg.Is<ServiceBusProcessorOptions>(opts => !opts.AutoCompleteMessages && opts.MaxConcurrentCalls == sbConfig.MaxConcurrentCalls));
        }
    }
}
