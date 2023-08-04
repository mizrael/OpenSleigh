namespace OpenSleigh.Utils
{
    public interface ITypeResolver
    {
        void Register(Type type);
        Type? Resolve(string typeName, bool throwOnError = true);
    }
}