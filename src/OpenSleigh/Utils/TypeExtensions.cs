using OpenSleigh.Transport;

namespace OpenSleigh.Utils;

internal static class TypeExtensions
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

    public static ISet<Type> GetInitiatorMessageType(this Type type)
    {
        var initiatorType = typeof(IStartedBy<>).GetGenericTypeDefinition();
        var interfaces = type.GetInterfaces();

        var result = new HashSet<Type>();
        foreach (var i in interfaces)
        {
            if (!i.IsGenericType)
                continue;

            var openGeneric = i.GetGenericTypeDefinition();
            if (!openGeneric.IsAssignableFrom(initiatorType))
                continue;

            var messageType = i.GetGenericArguments().First();
            result.Add(messageType);
        }

        return result;
    }
}