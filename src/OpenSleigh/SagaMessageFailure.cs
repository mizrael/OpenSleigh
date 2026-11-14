using OpenSleigh.Transport;
using System.Text.Json.Serialization;

namespace OpenSleigh;

/// <summary>
/// what was decided for a message whose handler failed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FailureDecision>))]
public enum FailureDecision
{
    /// <summary>the message is delivered again later.</summary>
    Retried,

    /// <summary>the compensation handler of the saga ran, and the message counts as processed.</summary>
    Compensated,

    /// <summary>the compensation handler of the saga failed too, and the message was dead-lettered.</summary>
    CompensationFailed,

    /// <summary>the message was dead-lettered.</summary>
    DeadLettered
}

/// <summary>
/// the record of one failed attempt to handle a message, and of what was decided for it.
/// </summary>
public sealed record SagaMessageFailure
{
    /// <summary>
    /// how many characters of a stack trace, or of a compensation error, are kept.
    /// </summary>
    public const int MaxErrorDetailsLength = 4000;

    public required string Id { get; init; }
    public required string InstanceId { get; init; }
    public required string CorrelationId { get; init; }
    public required string SagaType { get; init; }
    public required string MessageId { get; init; }
    public required string MessageType { get; init; }

    /// <summary>
    /// 1 for the first time the handler failed for the message.
    /// </summary>
    public required int Attempt { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
    public required string ExceptionType { get; init; }
    public required string ExceptionMessage { get; init; }
    public string? StackTrace { get; init; }
    public required FailureDecision Decision { get; init; }

    /// <summary>
    /// when the message is due again, for <see cref="FailureDecision.Retried"/>.
    /// </summary>
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>
    /// why the compensation did not go through, for <see cref="FailureDecision.CompensationFailed"/>.
    /// </summary>
    public string? CompensationError { get; init; }

    /// <param name="exception">the exception the message handler threw.</param>
    /// <param name="compensationError">the exception that stopped the compensation, if any.</param>
    public static SagaMessageFailure Create<TM>(
        ISagaInstance instance,
        IMessageContext<TM> messageContext,
        Exception exception,
        int attempt,
        FailureDecision decision,
        Exception? compensationError = null)
        where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(messageContext);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var sagaType = instance.Descriptor.SagaType;
        var messageType = typeof(TM);

        return new SagaMessageFailure
        {
#if NET9_0_OR_GREATER
            Id = Guid.CreateVersion7().ToString("N"),
#else
            Id = Guid.NewGuid().ToString("N"),
#endif
            InstanceId = instance.InstanceId,
            CorrelationId = instance.CorrelationId,
            SagaType = sagaType.FullName ?? sagaType.Name,
            MessageId = messageContext.MessageId,
            MessageType = messageType.FullName ?? messageType.Name,
            Attempt = attempt,
            OccurredAt = DateTimeOffset.UtcNow,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            ExceptionMessage = exception.Message,
            StackTrace = Truncate(exception.StackTrace),
            Decision = decision,
            CompensationError = Truncate(compensationError?.ToString())
        };
    }

    private static string? Truncate(string? text)
        => text is null || text.Length <= MaxErrorDetailsLength
            ? text
            : text[..MaxErrorDetailsLength];
}
