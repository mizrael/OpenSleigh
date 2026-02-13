using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Utils;

public class SerializedExtensionsTests
{
    [Fact]
    public void Deserialize_generic_should_return_typed_result()
    {
        var serializer = new OpenSleigh.Utils.JsonSerializer();
        var data = new TestRecord { Name = "test" };
        var bytes = serializer.Serialize(data);

        var result = serializer.Deserialize<TestRecord>(bytes);

        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public void Deserialize_generic_should_cast_correctly()
    {
        var serializer = new OpenSleigh.Utils.JsonSerializer();
        var data = new TestRecord { Name = "test" };
        var bytes = serializer.Serialize(data);

        // Deserialize as object first, then use the extension method
        var result = serializer.Deserialize<TestRecord>(bytes);
        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
    }

    private class TestRecord
    {
        public string Name { get; set; } = "";
    }
}
