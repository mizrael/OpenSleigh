using System;
using System.Collections.Generic;
using System.Reflection;

namespace OpenSleigh.Core
{
    public class TypeResolver : ITypeResolver
    {
        private readonly HashSet<Assembly> _assemblies = new HashSet<Assembly>();

        public void Register<T>() => Register(typeof(T));

        public void Register(Type type)
        {
            if (type == null) 
                throw new ArgumentNullException(nameof(type));
            _assemblies.Add(type.Assembly);
        }

        public Type Resolve(string typeName)
        {
            Type dataType = null;
            foreach (var assembly in _assemblies)
                dataType = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
            if (null == dataType)
                throw new TypeLoadException($"unable to resolve type '{typeName}'");
            return dataType;
        }
    }
}