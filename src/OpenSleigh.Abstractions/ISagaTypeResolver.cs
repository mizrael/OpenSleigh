using System;
using System.Collections.Generic;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISagaTypeResolver
    {
        /// <summary>
        /// returns the list of saga types with the associated saga state type that
        /// can handle the input message type.
        /// </summary>
        /// <typeparam name="TM">the message type.</typeparam>        
        IEnumerable<(Type sagaType, Type sagaStateType)> Resolve<TM>() where TM : IMessage;

        /// <summary>
        /// registers a saga type with the associated state type.
        /// </summary>
        /// <typeparam name="TS">the saga type.</typeparam>
        /// <typeparam name="TD">the saga state type.</typeparam>
        /// <returns>true if the saga can handle messages</returns>
        bool Register<TS, TD>() where TD : SagaState where TS : Saga<TD>;
    }
}