using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Outbox;

public class MockSerializer : ISerializer
{
    private readonly object? _result;
    private readonly byte[]? _bytes;

    public MockSerializer(object? result = null, byte[]? bytes = null)
    {
        _result = result;
        _bytes = bytes;
    }

    public byte[] Serialize(object data)
        => _bytes;


    public object? Deserialize(ReadOnlySpan<byte> data, Type returnType)
        => _result;
}