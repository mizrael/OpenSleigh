using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Transport;

public class MessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_should_throw_when_message_is_null()
    {
        var sagaRunner = Substitute.For<ISagaRunner>();
        var sagaDescriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        var serializer = Substitute.For<ISerializer>();

        var sut = new MessageProcessor(sagaRunner, sagaDescriptorsResolver, serializer);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await sut.ProcessAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessAsync_should_process_message_using_reflection()
    {
        // Arrange
        var sagaRunner = Substitute.For<ISagaRunner>();
        var sagaDescriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        var serializer = Substitute.For<ISerializer>();
        
        var message = new DummyMessage();
        var envelope = DummyMessage.CreateEnvelope();
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        sagaDescriptorsResolver.Resolve(Arg.Any<IMessage>()).Returns(new[] { descriptor });
        sagaRunner.ProcessAsync(Arg.Any<IMessageContext<DummyMessage>>(), descriptor, Arg.Any<CancellationToken>())
                  .Returns(ValueTask.CompletedTask);

        var sut = new MessageProcessor(sagaRunner, sagaDescriptorsResolver, serializer);

        // Act
        await sut.ProcessAsync(envelope, CancellationToken.None);

        // Assert
        sagaDescriptorsResolver.Received(1).Resolve(Arg.Any<IMessage>());
        await sagaRunner.Received(1).ProcessAsync(
            Arg.Any<IMessageContext<DummyMessage>>(), 
            descriptor, 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_should_process_multiple_descriptors()
    {
        // Arrange
        var sagaRunner = Substitute.For<ISagaRunner>();
        var sagaDescriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        var serializer = Substitute.For<ISerializer>();
        
        var envelope = DummyMessage.CreateEnvelope();
        var descriptor1 = SagaDescriptor.Create<FakeSaga>();
        var descriptor2 = SagaDescriptor.Create<FakeSagaWithState, int>();

        sagaDescriptorsResolver.Resolve(Arg.Any<IMessage>()).Returns(new[] { descriptor1, descriptor2 });

        var sut = new MessageProcessor(sagaRunner, sagaDescriptorsResolver, serializer);

        // Act
        await sut.ProcessAsync(envelope, CancellationToken.None);

        // Assert
        await sagaRunner.Received(2).ProcessAsync(
            Arg.Any<IMessageContext<DummyMessage>>(), 
            Arg.Any<SagaDescriptor>(), 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_should_use_correct_message_type_from_envelope()
    {
        // Arrange
        var sagaRunner = Substitute.For<ISagaRunner>();
        var sagaDescriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        var serializer = Substitute.For<ISerializer>();
        
        var envelope = DummyMessage.CreateEnvelope();
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        sagaDescriptorsResolver.Resolve(Arg.Any<IMessage>()).Returns(new[] { descriptor });

        var sut = new MessageProcessor(sagaRunner, sagaDescriptorsResolver, serializer);

        // Act
        await sut.ProcessAsync(envelope, CancellationToken.None);

        // Assert - verify the message type from the envelope is used correctly
        Assert.Equal(typeof(DummyMessage), envelope.MessageType);
        await sagaRunner.Received(1).ProcessAsync(
            Arg.Is<IMessageContext<DummyMessage>>(ctx => ctx.MessageId == envelope.MessageId),
            descriptor,
            Arg.Any<CancellationToken>());
    }
}
