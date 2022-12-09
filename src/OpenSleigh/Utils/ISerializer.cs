namespace OpenSleigh.Utils
{
    public interface ISerializer
    {
        byte[] Serialize<T>(T data);
        object? Deserialize(ReadOnlySpan<byte> data, Type returnType);
    }
}