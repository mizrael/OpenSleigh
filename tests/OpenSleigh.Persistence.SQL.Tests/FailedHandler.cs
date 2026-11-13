using Microsoft.Extensions.Logging.Abstractions;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Persistence.SQL.Tests;

internal record FailedHandlerMessages(MessageEnvelope PublishedByHandler, MessageEnvelope PublishedByRollback);

internal static class FailedHandler
{
    /// <summary>
    /// processes the message on a locked saga, with a handler that publishes a message and fails,
    /// and a rollback that publishes another one.
    /// </summary>
    public static async Task<FailedHandlerMessages> ProcessAsync(
        ISagaInstance saga,
        IMessageContext<FakeMessage> messageContext,
        ISagaStateRepository repository)
    {
        MessageEnvelope? publishedByHandler = null;
        MessageEnvelope? publishedByRollback = null;

        var handler = Substitute.For<IHandleMessage<FakeMessage>>();
        handler.HandleAsync(messageContext, Arg.Any<CancellationToken>())
               .Returns(_ =>
               {
                   publishedByHandler = Publish(saga);
                   return ValueTask.FromException(new InvalidOperationException("whoops"));
               });
        handler.RollbackAsync(messageContext, Arg.Any<CancellationToken>())
               .Returns(_ =>
               {
                   publishedByRollback = Publish(saga);
                   return ValueTask.CompletedTask;
               });

        var handlerFactory = Substitute.For<IMessageHandlerFactory>();
        handlerFactory.Create<FakeMessage>(saga).Returns(handler);
        var handlerManager = new MessageHandlerManager(handlerFactory, NullLogger<MessageHandlerManager>.Instance);
        var executionService = new SagaExecutionService(new SagaInstanceFactory(), repository, Substitute.For<IOutboxRepository>());

        await Assert.ThrowsAsync<SagaException>(
            async () => await saga.ProcessAsync(handlerManager, messageContext, executionService, CancellationToken.None));

        return new FailedHandlerMessages(publishedByHandler!, publishedByRollback!);
    }

    private static MessageEnvelope Publish(ISagaInstance saga)
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), saga);
        saga.Publish(envelope);
        return envelope;
    }
}
