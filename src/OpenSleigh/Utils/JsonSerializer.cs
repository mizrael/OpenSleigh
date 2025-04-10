using System.Text.Json;

namespace OpenSleigh.Utils;

public class JsonSerializer : ISerializer
{
    private static readonly JsonSerializerOptions Settings = new()
    {
        IgnoreReadOnlyFields = false,
        IgnoreReadOnlyProperties = false,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    public byte[] Serialize(object data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));

        var type = data.GetType();
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, type, Settings);
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType, nameof(returnType));

        return System.Text.Json.JsonSerializer.Deserialize(data, returnType, Settings);
    }      
}