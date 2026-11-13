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

        // this runs on every publish: reads come first, so that a type already known costs no write
        if (!_assemblies.ContainsKey(type.Assembly))
            _assemblies.TryAdd(type.Assembly, 0);

        // messages travel with the full name of their type. The short name is kept as a convenience.
        // For both, the first type registered with a name keeps it
        if (type.FullName is not null && !_typesByName.ContainsKey(type.FullName))
            _typesByName.TryAdd(type.FullName, type);
        if (!_typesByName.ContainsKey(type.Name))
            _typesByName.TryAdd(type.Name, type);
    }

    public Type? Resolve(string typeName, bool throwOnError = true)
    {
        if (_typesByName.TryGetValue(typeName, out var dataType))
            return dataType;

        foreach (var assembly in _assemblies)
        {
            dataType = FindIn(assembly.Key, typeName, throwOnError);
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

    // an assembly refuses some names outright, whatever it is told about errors: one that names an assembly,
    // or that it cannot parse. When errors are not wanted, those names are types that were not found
    private static Type? FindIn(Assembly assembly, string typeName, bool throwOnError)
    {
        try
        {
            return assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
        }
        catch (Exception e) when (!throwOnError && e is ArgumentException or TypeLoadException or IOException or BadImageFormatException)
        {
            return null;
        }
    }

    // type names arrive in message headers, and unlimited names resolve to a type: other spellings of one type
    // (padding, array and pointer suffixes) and constructed generics, which are a new type for every type argument.
    // Only the own full name of a type defined in a registered assembly is cached,
    // so the cache cannot grow beyond the types those assemblies define
    private static bool IsCanonicalName(string typeName, Type type)
        => !type.HasElementType &&
           !type.IsConstructedGenericType &&
           string.Equals(typeName, type.FullName, StringComparison.OrdinalIgnoreCase);
}