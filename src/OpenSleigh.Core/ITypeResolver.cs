using System;

namespace OpenSleigh.Core
{
    public interface ITypeResolver
    {
        Type Resolve(string typeName);
        void Register<T>();
        void Register(Type type);
    }
}