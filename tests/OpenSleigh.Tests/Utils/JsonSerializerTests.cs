namespace OpenSleigh.Tests.Utils;

public class JsonSerializerTests
{
    private readonly OpenSleigh.Utils.JsonSerializer _sut = new();

    [Fact]
    public void Serialize_should_throw_when_data_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Serialize(null!));
    }

    [Fact]
    public void Serialize_and_Deserialize_should_roundtrip()
    {
        var data = new TestData { Name = "test", Value = 42 };

        var bytes = _sut.Serialize(data);
        var result = _sut.Deserialize(bytes, typeof(TestData)) as TestData;

        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Deserialize_should_throw_when_returnType_is_null()
    {
        var bytes = new byte[] { 1, 2, 3 };
        Assert.Throws<ArgumentNullException>(() => _sut.Deserialize(bytes, null!));
    }

    [Fact]
    public void Serialize_should_include_fields()
    {
        var data = new DataWithField { Field = "fieldValue" };

        var bytes = _sut.Serialize(data);
        var result = _sut.Deserialize(bytes, typeof(DataWithField)) as DataWithField;

        Assert.NotNull(result);
        Assert.Equal("fieldValue", result.Field);
    }

    private class TestData
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    private class DataWithField
    {
        public string Field = "";
    }
}
