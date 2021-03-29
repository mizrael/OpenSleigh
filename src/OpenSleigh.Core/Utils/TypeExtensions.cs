using System;
using System.Collections.Generic;
using System.Linq;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Utils
{
    public static class TypeExtensions
    {
        public static IEnumerable<Type> GetHandledMessageTypes(this Type type)
        {
            var messageHandlerType = typeof(IHandleMessage<>).GetGenericTypeDefinition();
            var interfaces = type.GetInterfaces();
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

        public static bool CanHandleMessage<TM>(this Type type) where TM : IMessage {
            var messageHandlerType = typeof(IHandleMessage<TM>);
            return type.IsAssignableTo(messageHandlerType);
        }

        public static bool IsEvent(this Type type)
            => type.IsAssignableTo(typeof(IEvent));

        public static bool IsSaga(this Type type)
            => IsDerivedOfGenericType(type, typeof(Saga<>));

        static bool IsDerivedOfGenericType(Type type, Type genericType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
                return true;
            if (type.BaseType != null)            
                return IsDerivedOfGenericType(type.BaseType, genericType);            
            return false;
        }
    }
}