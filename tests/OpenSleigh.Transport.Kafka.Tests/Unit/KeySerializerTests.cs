using System.Text;
using System.Text.Json;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KeySerializerTests
{
    [Fact]
    public void Serialize_should_return_bytes_from_guid()
    {
        var sut = new KeySerializer<Guid>();

        var expectedGuid = Guid.NewGuid();
        var result = sut.Serialize(expectedGuid, default);
        Assert.Equal(expectedGuid.ToByteArray(), result);
    }

    [Fact]
    public void Serialize_should_return_the_bytes_of_a_string_as_the_consumer_reads_them_back()
    {
        var sut = new KeySerializer<string>();

        var result = sut.Serialize("lorem-ipsum", default);

        Assert.Equal("lorem-ipsum", Confluent.Kafka.Deserializers.Utf8.Deserialize(result, false, default));
    }

    [Fact]
    public void Serialize_should_return_json_bytes_from_class()
    {
        var sut = new KeySerializer<DummyMessage>();

        var message = new DummyMessage();
        var jsonMessage = JsonSerializer.Serialize(message);
        var expectedData = Encoding.UTF8.GetBytes(jsonMessage);

        var result = sut.Serialize(message, default);
        Assert.Equal(expectedData, result);
    }
}
