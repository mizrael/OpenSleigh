using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using NSubstitute;
using OpenSleigh.Core;
using OpenSleigh.Core.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public class ServiceBusPublisherTests
    {
        [Fact]
        public void ctor_should_throw_when_input_invalid()
        {
            var senderFactory = NSubstitute.Substitute.For<IServiceBusSenderFactory>();
            var serializer = NSubstitute.Substitute.For<ITransportSerializer>();
            var logger = NSubstitute.Substitute.For<ILogger<ServiceBusPublisher>>();
            var systemInfo = NSubstitute.Substitute.For<ISystemInfo>();

            Assert.Throws<ArgumentNullException>(() => new ServiceBusPublisher(null, serializer, logger, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusPublisher(senderFactory, null, logger, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusPublisher(senderFactory, serializer, null, systemInfo));
            Assert.Throws<ArgumentNullException>(() => new ServiceBusPublisher(senderFactory, serializer, logger, null));
        }

        [Fact]
        public async Task PublishAsync_should_throw_when_input_null()
        {
            var senderFactory = NSubstitute.Substitute.For<IServiceBusSenderFactory>();
            var serializer = NSubstitute.Substitute.For<ITransportSerializer>();
            var logger = NSubstitute.Substitute.For<ILogger<ServiceBusPublisher>>();
            var systemInfo = NSubstitute.Substitute.For<ISystemInfo>();

            var sut = new ServiceBusPublisher(senderFactory, serializer, logger, systemInfo);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(null));
        }

        [Fact]
        public async Task PublishAsync_should_send_message()
        {
            var message = DummyMessage.New();

            var sender = NSubstitute.Substitute.For<ServiceBusSender>();
            sender.FullyQualifiedNamespace.Returns("FullyQualifiedNamespace");
            sender.EntityPath.Returns("EntityPath");
            sender.SendMessageAsync(null).ReturnsForAnyArgs(Task.CompletedTask);

            var senderFactory = NSubstitute.Substitute.For<IServiceBusSenderFactory>();
            senderFactory.Create(message).ReturnsForAnyArgs(sender);

            var serializedMessage = new byte[] { 1, 2, 3 };
            var serializer = NSubstitute.Substitute.For<ITransportSerializer>();
            serializer.Serialize(message).Returns(serializedMessage);

            var logger = NSubstitute.Substitute.For<ILogger<ServiceBusPublisher>>();
            
            var systemInfo = NSubstitute.Substitute.For<ISystemInfo>();
            systemInfo.ClientId.Returns(Guid.NewGuid());

            var sut = new ServiceBusPublisher(senderFactory, serializer, logger, systemInfo);
                        
            await sut.PublishAsync(message);

            await sender.Received(1)
                .SendMessageAsync(Arg.Is<ServiceBusMessage>(msg => msg.CorrelationId == message.CorrelationId.ToString() &&
                msg.MessageId == message.Id.ToString() &&
                msg.ApplicationProperties.ContainsKey(HeaderNames.MessageType) &&
                (string)msg.ApplicationProperties[HeaderNames.MessageType] == message.GetType().FullName),
                Arg.Any<CancellationToken>());
        }
    }
}
