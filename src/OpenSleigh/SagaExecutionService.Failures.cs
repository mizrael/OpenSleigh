using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using System.Runtime.ExceptionServices;

namespace OpenSleigh;

// what happens to a message whose handler failed
public partial class SagaExecutionService
{
    // no retry policy yet: a handler runs once for a message
    private const int FirstAttempt = 1;

    public async ValueTask HandleFailureAsync<TM>(
        ISagaInstance context,
        IMessageContext<TM> messageContext,
        IMessageHandlerManager messageHandlerManager,
        SagaException failure,
        CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(messageContext);
        ArgumentNullException.ThrowIfNull(messageHandlerManager);
        ArgumentNullException.ThrowIfNull(failure);

        var handlerError = failure.InnerException ?? failure;

        if (!context.Descriptor.CompensatedMessageTypes.Contains(typeof(TM)))
        {
            await AbandonAsync(
                context,
                SagaMessageFailure.Create(context, messageContext, handlerError, FirstAttempt, FailureDecision.DeadLettered))
                .ConfigureAwait(false);

            ExceptionDispatchInfo.Throw(failure);
        }

        await CompensateAsync(context, messageContext, messageHandlerManager, handlerError, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask CompensateAsync<TM>(
        ISagaInstance failed,
        IMessageContext<TM> messageContext,
        IMessageHandlerManager messageHandlerManager,
        Exception handlerError,
        CancellationToken cancellationToken)
        where TM : IMessage
    {
        // whoever holds the lock at the moment: the instance the handler failed on, until the stored one takes over
        var locked = failed;

        try
        {
            // nothing of the failed handler was stored: what is read now is the saga as it was before the message
            var stored = await _sagaStateRepository.FindAsync(failed.Descriptor, messageContext, cancellationToken)
                                                   .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"saga '{failed.InstanceId}' is not stored any more.");

            failed.HandLockTo(stored);
            locked = stored;

            await messageHandlerManager.CompensateAsync(
                stored,
                messageContext,
                new MessageFailure(handlerError, FirstAttempt),
                cancellationToken).ConfigureAwait(false);

            stored.RecordFailure(
                SagaMessageFailure.Create(stored, messageContext, handlerError, FirstAttempt, FailureDecision.Compensated));
            stored.SetAsProcessed(messageContext);

            await CommitAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        // interrupted by a stop, or the saga was taken over by someone else: the message is delivered again
        catch (Exception interruption) when (IsStop(interruption, cancellationToken) || interruption.IsLockConflict())
        {
            await AbandonAsync(locked, failure: null).ConfigureAwait(false);
            throw;
        }
        catch (Exception compensationError)
        {
            _logger.LogError(
                compensationError,
                "the compensation of message '{MessageId}' on Saga '{SagaType}/{InstanceId}' has failed: {Error}",
                messageContext.MessageId,
                failed.Descriptor.SagaType,
                failed.InstanceId,
                compensationError.Message);

            await AbandonAsync(
                locked,
                SagaMessageFailure.Create(
                    locked, messageContext, handlerError, FirstAttempt, FailureDecision.CompensationFailed, compensationError))
                .ConfigureAwait(false);

            throw new SagaException(
                $"message '{messageContext.MessageId}' could not be processed by Saga '{failed.Descriptor.SagaType}/{failed.InstanceId}' " +
                $"and its compensation has failed too : {compensationError.Message}",
                new AggregateException(handlerError, compensationError));
        }
    }

    private static bool IsStop(Exception exception, CancellationToken cancellationToken)
        => exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    // not cancellable: the decision is taken, and the saga must not stay locked because the application is stopping
    private async ValueTask AbandonAsync(ISagaInstance instance, SagaMessageFailure? failure)
    {
        instance.ClearOutbox();
        instance.RecordFailure(failure);

        try
        {
            await _sagaStateRepository.AbandonAsync(instance, CancellationToken.None)
                                      .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // the failure that led here is the one to report. The lock expires by itself where locks expire
            _logger.LogError(
                e,
                "Saga '{SagaType}/{InstanceId}' could not be released after a failure, and the failure was not recorded: {Error}",
                instance.Descriptor.SagaType,
                instance.InstanceId,
                e.Message);
        }
    }
}
