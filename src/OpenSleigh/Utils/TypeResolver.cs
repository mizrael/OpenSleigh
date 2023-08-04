using System.Collections.Concurrent;
using System.Reflection;

namespace OpenSleigh.Utils
{
    public class TypeResolver : ITypeResolver
    {
        private readonly HashSet<Assembly> _assemblies = new();
        private readonly ConcurrentDictionary<string, Type> _typesByName = new();

        public void Register(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            if(_assemblies.Contains(type.Assembly))
                return;

            _assemblies.Add(type.Assembly);
            _typesByName.TryAdd(type.Name.ToLower(), type);
        }

        public Type? Resolve(string typeName, bool throwOnError = true)
        {
            Type? dataType = null;
            foreach (var assembly in _assemblies)
            {
                dataType = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
                if (dataType is not null)
                    break;
            }

            if (dataType is null)
                _typesByName.TryGetValue(typeName.ToLower(), out dataType);

            if (dataType is null && throwOnError)
                throw new TypeLoadException($"unable to resolve type '{typeName}'");

            return dataType;
        }
    }
}