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
    public void Serialize_should_write_a_string_as_json_once_however_many_times_it_goes_through_a_consumer()
    {
        var sut = new KeySerializer<string>();

        var published = sut.Serialize("lorem-ipsum", default);
        var consumed = Confluent.Kafka.Deserializers.Utf8.Deserialize(published, false, default);
        var publishedAgain = sut.Serialize(consumed, default);

        // the format 3.3.0 writes, and the id its consumers have stored as processed
        Assert.Equal("\"lorem-ipsum\"", consumed);
        Assert.Equal(published, publishedAgain);
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
