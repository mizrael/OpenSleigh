using System.Text.Json;

namespace OpenSleigh.Utils
{
    internal class JsonSerializer : ISerializer
    {
        private static readonly JsonSerializerOptions Settings = new()
        {
            IgnoreReadOnlyFields = false,
            IgnoreReadOnlyProperties = false,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };

        public byte[] Serialize<T>(T data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));

            var type = data.GetType();
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, type, Settings);
        }

        public object? Deserialize(ReadOnlySpan<byte> data, Type returnType)
        {
            if (returnType is null)
                throw new ArgumentNullException(nameof(returnType));

            return System.Text.Json.JsonSerializer.Deserialize(data, returnType, Settings);
        }
    }
}