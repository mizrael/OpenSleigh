namespace OpenSleigh.Persistence.Mongo.Tests;

/// <summary>
/// makes a commit fail half way: whatever comes before a <typeparamref name="T"/> is serialized, and it is not.
/// </summary>
internal sealed class SerializerFailingOn<T> : ISerializer
{
    private readonly JsonSerializer _inner = new();

    public byte[] Serialize(object data)
        => data is T
            ? throw new InvalidOperationException($"a '{typeof(T).Name}' cannot be serialized")
            : _inner.Serialize(data);

    public object? Deserialize(ReadOnlySpan<byte> data, Type returnType)
        => _inner.Deserialize(data, returnType);
}
