using OpenSleigh.Utils;

namespace OpenSleigh
{
    public record SagaDescriptor
    {
        public SagaDescriptor(Type sagaType, Type? sagaStateType = null)
        {
            var initiatorType = sagaType.GetInitiatorMessageType();
            
            SagaType = sagaType ?? throw new ArgumentNullException(nameof(sagaType));

            // TODO: this check could go into a Roslyn analyzer                
            InitiatorType = initiatorType ?? throw new MissingMethodException($"saga type '{sagaType.FullName}' does not implement any initiator.");
            
            SagaStateType = sagaStateType;
        }

        /// <summary>
        /// the saga type.
        /// </summary>
        public Type SagaType { get; }

        /// <summary>
        /// type of the message that can start this saga.
        /// </summary>
        public Type InitiatorType { get; }

        /// <summary>
        /// optional. type of the custom saga state.
        /// </summary>
        public Type? SagaStateType { get; }

        public static SagaDescriptor Create<TS>() where TS : ISaga
            => new SagaDescriptor(typeof(TS));

        public static SagaDescriptor Create<TS, TD>()
            where TS : ISaga<TD>
            where TD : new()
            => new SagaDescriptor(typeof(TS), typeof(TD));
    }
}