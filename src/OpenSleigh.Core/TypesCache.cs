using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace OpenSleigh.Core
{
    public class TypesCache : ITypesCache
    {
        private readonly ConcurrentDictionary<string, Type> _types = new ConcurrentDictionary<string, Type>();
        private readonly ConcurrentDictionary<string, MethodInfo> _methods = new ConcurrentDictionary<string, MethodInfo>();

        public Type GetGeneric(Type baseType, params Type[] args)
        {
            var sb = new StringBuilder();
            sb.Append(baseType.FullName);
            foreach (var t in args)
                sb.Append(t.FullName);

            var key = sb.ToString();

            return _types.GetOrAdd(key, k => baseType.MakeGenericType(args));
        }

        public MethodInfo GetMethod(Type type, string name, Type[] args = null)
        {
            var sb = new StringBuilder();
            sb.Append(type.FullName);
            if (args is not null)
                foreach (var t in args)
                    sb.Append(t.FullName);
            var key = sb.ToString();

            return _methods.GetOrAdd(key, k => (args is null) ? type.GetMethod(name) : type.GetMethod(name, args));
        }
    }
}