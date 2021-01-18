using System;

namespace OpenSleigh.Core
{
    public interface ITypeResolver
    {
        Type Resolve(string typeName);
        void Register(Type type);
    }
}