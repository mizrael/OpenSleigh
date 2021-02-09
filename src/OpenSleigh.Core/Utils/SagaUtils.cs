using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenSleigh.Core.Utils
{
    public static class SagaUtils<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        public static IEnumerable<Type> GetHandledMessageTypes()
          
        {
            var sagaType = typeof(TS);
            var messageHandlerType = typeof(IHandleMessage<>).GetGenericTypeDefinition();
            var interfaces = sagaType.GetInterfaces();
            foreach (var i in interfaces)
            {
                if (!i.IsGenericType)
                    continue;

                var openGeneric = i.GetGenericTypeDefinition();
                if (!openGeneric.IsAssignableFrom(messageHandlerType))
                    continue;

                var messageType = i.GetGenericArguments().First();
                yield return messageType;
            }
        }
    }
}