using System;
using System.Collections.Generic;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISagaTypeResolver
    {
        IEnumerable<(Type sagaType, Type sagaStateType)> Resolve<TM>() where TM : IMessage;
        bool Register<TS, TD>() where TD : SagaState where TS : Saga<TD>;
        IReadOnlyCollection<Type> GetSagaTypes();
    }
}