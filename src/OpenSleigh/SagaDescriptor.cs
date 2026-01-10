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

        SagaStateType = sagaStateType;
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