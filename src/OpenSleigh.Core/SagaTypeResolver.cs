using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public class SagaTypeResolver : ISagaTypeResolver
    {
        private readonly ITypeResolver _typeResolver;
        private readonly ConcurrentDictionary<Type, IList<(Type sagaType, Type sagaStateType)>> _types = new ();

        private static readonly Type MessageHandlerType = typeof(IHandleMessage<>).GetGenericTypeDefinition();

        public SagaTypeResolver(ITypeResolver typeResolver)
        {
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        /// <inheritdoc/>
        public IEnumerable<(Type sagaType, Type sagaStateType)> Resolve<TM>() where TM : IMessage
        {
            var messageType = typeof(TM);

            _types.TryGetValue(messageType, out var types);
            return types ?? Enumerable.Empty<(Type sagaType, Type sagaStateType)>();
        }

        /// <inheritdoc/>
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
            var isEvent = messageType.IsAssignableTo(typeof(IEvent));

            // if it's not an event, we allow only one handler
            if (!isEvent && _types.ContainsKey(messageType))
                throw new TypeAccessException($"there is already a saga for message type '{messageType.FullName}'");

            _types.AddOrUpdate(messageType,
                (k) =>
                {
                    _typeResolver.Register(messageType);
                    var res = new List<(Type sagaType, Type sagaStateType)> {types};
                    return res;
                },
                (k, v) =>
                {
                    v.Add(types);
                    return v;
                });
        }
    }
}