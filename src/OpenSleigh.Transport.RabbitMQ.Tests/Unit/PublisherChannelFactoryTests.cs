using FluentAssertions;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class PublisherChannelFactoryTests
    {
        [Fact]
        public void Create_should_return_valid_context()
        {
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var references = new QueueReferences("exchange", "queue", "deadletterExch", "deadLetterQ");
            factory.Create(null)
                .ReturnsForAnyArgs(references);
            
            var sut = new PublisherChannelFactory(connection, factory);

            var message = DummyMessage.New();
            var result = sut.Create(message);
            result.Should().NotBeNull();
            result.Channel.Should().NotBeNull();
            result.QueueReferences.Should().Be(references);
        }
    }
}