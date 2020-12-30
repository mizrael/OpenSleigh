using System;
using System.Reflection;

namespace OpenSleigh.Core
{
    public interface ITypesCache
    {
        Type GetGeneric(Type baseType, params Type[] args);
        MethodInfo GetMethod(Type type, string name, Type[] args = null);
    }
}