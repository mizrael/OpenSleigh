using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    //TODO: rename
    public class SagaTypeResolver : ISagaTypeResolver
    {
        private readonly ConcurrentDictionary<Type, (Type sagaType, Type sagaStateType)> _types = new ();

        private static readonly Type MessageHandlerType = typeof(IHandleMessage<>).GetGenericTypeDefinition();

        //TODO: this should return a collection of saga/state types
        public (Type sagaType, Type sagaStateType) Resolve<TM>() where TM : IMessage
        {
            var messageType = typeof(TM);

            _types.TryGetValue(messageType, out var types);
            return types;
        }

        public bool Register<TS, TD>() where TD : SagaState where TS : Saga<TD>
        {
            var hasMessages = false;

            var sagaType = typeof(TS);
            var sagaStateType = typeof(TD);
            
            var interfaces = sagaType.GetInterfaces();
            foreach (var i in interfaces)
            {
                if (!i.IsGenericType)
                    continue;

                var openGeneric = i.GetGenericTypeDefinition();
                if (!openGeneric.IsAssignableFrom(MessageHandlerType))
                    continue;

                var messageType = i.GetGenericArguments().First();

                Register(messageType, (sagaType, sagaStateType));

                hasMessages = true;
            }

            return hasMessages;
        }
        private void Register(Type messageType, (Type sagaType, Type sagaStateType) types)
        {
            //TODO: if message is an event, allow multiple sagas types

            var isEvent = messageType.IsAssignableTo(typeof(IEvent));

            if (!isEvent && _types.ContainsKey(messageType))
                throw new TypeAccessException($"there is already a saga for message type '{messageType.FullName}'");

            _types.AddOrUpdate(messageType, types, (k, v) => types);
        }

        public IReadOnlyCollection<Type> GetSagaTypes() => _types.Values.Select(v => v.sagaStateType).ToImmutableList();
    }
}