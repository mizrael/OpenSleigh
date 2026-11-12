using System.Collections.Concurrent;
using System.Reflection;

namespace OpenSleigh.Utils;

public class TypeResolver : ITypeResolver
{
    private readonly ConcurrentDictionary<Assembly, byte> _assemblies = new();
    private readonly ConcurrentDictionary<string, Type> _typesByName = new(StringComparer.OrdinalIgnoreCase);

    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        _assemblies.TryAdd(type.Assembly, 0);

        // messages travel with the full name of their type, which is unique.
        // The short name is kept as a convenience: the first type registered with it wins
        if (type.FullName is not null)
            _typesByName[type.FullName] = type;
        _typesByName.TryAdd(type.Name, type);
    }

    public Type? Resolve(string typeName, bool throwOnError = true)
    {
        if (_typesByName.TryGetValue(typeName, out var dataType))
            return dataType;

        foreach (var assembly in _assemblies.Keys)
        {
            dataType = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
            if (dataType is not null)
                break;
        }

        if (dataType is null && throwOnError)
            throw new TypeLoadException($"unable to resolve type '{typeName}'");

        if (dataType is not null && IsCanonicalName(typeName, dataType))
            _typesByName.TryAdd(typeName, dataType);

        return dataType;
    }

    internal int CachedNamesCount => _typesByName.Count;

    // type names arrive in message headers, and one type answers to unlimited spellings
    // (padding, array and pointer suffixes, generic arguments): only its own full name is cached,
    // so the cache cannot grow beyond the types of the registered assemblies
    private static bool IsCanonicalName(string typeName, Type type)
        => !type.HasElementType &&
           string.Equals(typeName, type.FullName, StringComparison.OrdinalIgnoreCase);
}