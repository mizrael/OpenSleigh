using OpenSleigh.Transport;

namespace OpenSleigh
{
    public interface ISagaDescriptorsResolver
    {
        /// <summary>
        /// returns the list of registered message types.
        /// </summary>
        /// <returns></returns>
        IEnumerable<Type> GetRegisteredMessageTypes();

        /// <summary>
        /// returns the list of saga types with the associated saga state type that
        /// can handle the input message.
        /// </summary>  
        IEnumerable<SagaDescriptor> Resolve(IMessage message);

        /// <summary>
        /// registers a saga type with the associated state type.
        /// </summary>
        /// <typeparam name="TS">the saga type.</typeparam>
        /// <typeparam name="TD">the saga state type.</typeparam>
        void Register<TS, TD>() 
            where TD : new()
            where TS : ISaga<TD>;

        /// <summary>
        /// registers a saga type
        /// </summary>
        /// <typeparam name="TS">the saga type.</typeparam>
        void Register<TS>()
            where TS : ISaga;
    }
}