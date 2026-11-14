using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh;

public record SagaDescriptor
{
    private SagaDescriptor(Type sagaType, Type? sagaStateType = null)
    {
        ArgumentNullException.ThrowIfNull(sagaType);

        if (!sagaType.IsAssignableTo(typeof(ISaga)))
            throw new ArgumentException($"saga type '{sagaType.FullName}' does not implement {nameof(ISaga)}.", nameof(sagaType));
        SagaType = sagaType ?? throw new ArgumentNullException(nameof(sagaType));

        InitiatorTypes = sagaType.GetInitiatorMessageType();
        if (!InitiatorTypes.Any())
            throw new MissingMethodException($"saga type '{sagaType.FullName}' does not implement any initiator.");

        ThrowIfDeclaresLegacyRollback(sagaType);

        CompensatedMessageTypes = sagaType.GetCompensatedMessageTypes();
        ThrowIfCompensatesUnhandledMessage(sagaType, CompensatedMessageTypes);

        SagaStateType = sagaStateType;
    }

    // version 3 called RollbackAsync when a handler failed. The method still compiles and would never run:
    // the saga is refused instead of silently losing what the method did
    private static void ThrowIfDeclaresLegacyRollback(Type sagaType)
    {
        var rollback = sagaType.GetLegacyRollbackMethods().FirstOrDefault();
        if (rollback is null)
            return;

        var messageType = rollback.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        throw new InvalidOperationException(
            $"saga type '{sagaType.FullName}' declares {rollback.Name} for '{messageType.FullName}', which is no longer called when a handler fails. " +
            $"Implement {nameof(ICompensateMessage<IMessage>)}<{messageType.Name}> and move the code to {nameof(ICompensateMessage<IMessage>.CompensateAsync)}, " +
            "which runs on the state as it was before the failed handler.");
    }

    private static void ThrowIfCompensatesUnhandledMessage(Type sagaType, ISet<Type> compensatedMessageTypes)
    {
        var handledMessageTypes = sagaType.GetHandledMessageTypes().ToHashSet();
        var unhandled = compensatedMessageTypes.FirstOrDefault(type => !handledMessageTypes.Contains(type));
        if (unhandled is not null)
            throw new ArgumentException(
                $"saga type '{sagaType.FullName}' compensates '{unhandled.FullName}' but does not handle it.", nameof(sagaType));
    }

    /// <summary>
    /// the saga type.QueryHintInterceptor
    /// </summary>
    public Type SagaType { get; }

    /// <summary>
    /// type of the message that can start this saga.
    /// </summary>
    public ISet<Type> InitiatorTypes { get; }

    /// <summary>
    /// types of the messages the saga has a compensation handler for (<see cref="ICompensateMessage{TM}"/>).
    /// </summary>
    public ISet<Type> CompensatedMessageTypes { get; }

    /// <summary>
    /// optional. type of the custom saga state.
    /// </summary>
    public Type? SagaStateType { get; }

    public static SagaDescriptor Create<TSaga>() where TSaga : ISaga
        => new SagaDescriptor(typeof(TSaga));

    public static SagaDescriptor Create<TSaga, TState>()
        where TSaga : ISaga<TState>
        where TState : new()
        => new SagaDescriptor(typeof(TSaga), typeof(TState));
}