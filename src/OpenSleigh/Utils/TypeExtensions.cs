using OpenSleigh.Transport;
using System.Reflection;

namespace OpenSleigh.Utils;

internal static class TypeExtensions
{
    public static IEnumerable<Type> GetHandledMessageTypes(this Type type)
        => type.GetMessageTypesOf(typeof(IHandleMessage<>));

    public static ISet<Type> GetCompensatedMessageTypes(this Type type)
        => type.GetMessageTypesOf(typeof(ICompensateMessage<>)).ToHashSet();

    public static ISet<Type> GetInitiatorMessageType(this Type type)
        => type.GetMessageTypesOf(typeof(IStartedBy<>)).ToHashSet();

    /// <summary>
    /// the methods a saga written for version 3 declared to undo a failed handler. Nothing calls them any more.
    /// </summary>
    public static IEnumerable<MethodInfo> GetLegacyRollbackMethods(this Type type)
        => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
               .Where(IsLegacyRollback);

    private const string LegacyRollbackMethodName = "RollbackAsync";

    private static bool IsLegacyRollback(MethodInfo method)
    {
        // an explicit implementation of something else is named after its interface
        if (method.Name != LegacyRollbackMethodName && !method.Name.EndsWith("." + LegacyRollbackMethodName, StringComparison.Ordinal))
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length != 2 || parameters[1].ParameterType != typeof(CancellationToken))
            return false;

        var contextType = parameters[0].ParameterType;
        return contextType.IsGenericType && contextType.GetGenericTypeDefinition() == typeof(IMessageContext<>);
    }

    private static IEnumerable<Type> GetMessageTypesOf(this Type type, Type openInterface)
    {
        foreach (var i in type.GetInterfaces())
        {
            if (!i.IsGenericType)
                continue;

            var openGeneric = i.GetGenericTypeDefinition();
            if (!openGeneric.IsAssignableFrom(openInterface))
                continue;

            yield return i.GetGenericArguments().First();
        }
    }
}