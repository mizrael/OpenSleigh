using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OpenSleigh.Core.DependencyInjection
{
    //TODO: rename
    public class SagaTypeResolver : ISagaTypeResolver
    {
        private readonly ConcurrentDictionary<Type, (Type sagaType, Type sagaStateType)> _types = new ();

        //TODO: this should return a collection of saga/state types
        public (Type sagaType, Type sagaStateType) Resolve<TM>() where TM : IMessage
        {
            var messageType = typeof(TM);

            _types.TryGetValue(messageType, out var types);
            return types;
        }

        public void Register(Type messageType, (Type sagaType, Type sagaStateType) types)
        {
            //TODO: if message is an event, allow multiple sagas types
            //TODO: throw only if message is a command and there is already a saga registered
            
            if (_types.ContainsKey(messageType))
                throw new TypeAccessException($"there is already a saga for message type '{messageType.FullName}'");

            _types.AddOrUpdate(messageType, types, (k, v) => types);
        }

        public IReadOnlyCollection<Type> GetMessageTypes() => _types.Keys.ToImmutableList();
        public IReadOnlyCollection<Type> GetSagaTypes() => _types.Values.Select(v => v.sagaStateType).ToImmutableList();
    }
}