using FluentAssertions;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests
{

    public class SagaExecutionContextTests
    {
        [Fact]
        public void CanProcess_should_return_true_when_message_not_processed()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var message = new FakeSagaStarter();
            
            var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

            var sut = new SagaExecutionContext("lorem", "ipsum", messageContext.CorrelationId, descriptor);

            sut.CanProcess(messageContext).Should().BeTrue();
        }

        [Fact]
        public void CanProcess_should_return_false_when_saga_completed()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var message = new FakeSagaStarter();
            var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

            var sut = new SagaExecutionContext("lorem", "ipsum", messageContext.CorrelationId, descriptor);
            sut.MarkAsCompleted();

            sut.CanProcess(messageContext).Should().BeFalse();
        }

        [Fact]
        public void CanProcess_should_return_true_when_correlation_different()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var message = new FakeSagaStarter();

            var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

            var sut = new SagaExecutionContext("lorem", "ipsum", Guid.NewGuid().ToString(), descriptor);

            sut.CanProcess(messageContext).Should().BeFalse();
        }

        [Fact]
        public void CanProcess_should_return_true_when_message_from_another_saga_and_initiator()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var messageContext = FakeMessageContext<FakeSagaStarter>.Create(                
                new FakeSagaStarter(),
                senderId: Guid.NewGuid().ToString());

            var sut = new SagaExecutionContext("lorem", "ipsum", messageContext.CorrelationId, descriptor);
            sut.CanProcess(messageContext).Should().BeTrue();
        }

        [Fact]
        public void CanProcess_should_return_true_when_parent_message_processed_and_sender_is_instance()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var correlationId = Guid.NewGuid().ToString();

            var sut = new SagaExecutionContext("lorem", "ipsum", correlationId, descriptor);

            var parentMessageContext = FakeMessageContext<FakeSagaMessage>.Create(                
                new FakeSagaMessage(),
                correlationId: correlationId,
                parentId: Guid.NewGuid().ToString(),
                senderId: sut.InstanceId);

            sut.SetAsProcessed(parentMessageContext);

            var messageContext = FakeMessageContext<FakeSagaMessage>.Create(
                new FakeSagaMessage(),
                correlationId: correlationId,
                parentId: parentMessageContext.Id,
                senderId: sut.InstanceId);

            sut.CanProcess(messageContext).Should().BeTrue();
        }

        [Fact]
        public void CanProcess_should_return_false_when_sender_is_not_instance_and_message_not_initiator_and_correlation_different()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var messageContext = FakeMessageContext<FakeSagaMessage>.Create(
                new FakeSagaMessage(),
                senderId: Guid.NewGuid().ToString());

            var sut = new SagaExecutionContext("lorem", "ipsum", correlationId: Guid.NewGuid().ToString(), descriptor);

            sut.CanProcess(messageContext).Should().BeFalse();
        }

        [Fact]
        public void CanProcess_should_return_true_when_sender_is_not_instance_and_message_not_initiator_and_same_correlation()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var messageContext = FakeMessageContext<FakeSagaMessage>.Create(
                new FakeSagaMessage(),
                senderId: Guid.NewGuid().ToString());

            var sut = new SagaExecutionContext("lorem", "ipsum", messageContext.CorrelationId, descriptor);

            sut.CanProcess(messageContext).Should().BeTrue();
        }

        [Fact]
        public void CanProcess_should_return_false_when_message_already_processed()
        {
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var message = new FakeSagaStarter();
            var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

            var sut = new SagaExecutionContext("lorem", "ipsum", messageContext.CorrelationId, descriptor);
            sut.SetAsProcessed(messageContext);

            sut.CanProcess(messageContext).Should().BeFalse();
        }
    }
}