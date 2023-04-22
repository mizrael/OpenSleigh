namespace OpenSleigh.Utils
{
    public interface ISerializer
    {
        byte[] Serialize(object data);
        object? Deserialize(ReadOnlySpan<byte> data, Type returnType);
    }
}