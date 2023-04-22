namespace OpenSleigh.Utils
{
    public static class SerializedExtensions
    {
        public static T? Deserialize<T>(this ISerializer serializer, ReadOnlySpan<byte> data)
        {
            var result = serializer.Deserialize(data, typeof(T));
            return (result is T casted) ? casted : default;
        }
    }

}