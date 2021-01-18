using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace OpenSleigh.Core
{
    public class TypeResolver : ITypeResolver
    {
        private readonly HashSet<Assembly> _assemblies = new ();
        private readonly ConcurrentDictionary<string, Type> _typesByName = new();
        
        public void Register(Type type)
        {
            if (type == null) 
                throw new ArgumentNullException(nameof(type));
            _assemblies.Add(type.Assembly);
            _typesByName.TryAdd(type.Name.ToLower(), type);
        }

        public Type Resolve(string typeName)
        {
            Type dataType = null;
            foreach (var assembly in _assemblies)
                dataType = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);

            if (dataType is null)
                _typesByName.TryGetValue(typeName.ToLower(), out dataType);
            
            if (dataType is null)
                throw new TypeLoadException($"unable to resolve type '{typeName}'");
            
            return dataType;
        }
    }
}