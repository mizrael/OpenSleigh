using FluentAssertions;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class QueueReferenceFactoryTests
    {
        [Fact]
        public void Create_should_return_valid_references()
        {
            var sut = new QueueReferenceFactory();
            var message = DummyMessage.New();
            var result = sut.Create(message);
            result.Should().NotBeNull();
            result.ExchangeName.Should().Be("dummymessage");
            result.QueueName.Should().Be("dummymessage.workers");
            result.DeadLetterExchangeName.Should().Be("dummymessage.dead");
            result.DeadLetterQueue.Should().Be("dummymessage.dead.workers");
        }
    }
}
