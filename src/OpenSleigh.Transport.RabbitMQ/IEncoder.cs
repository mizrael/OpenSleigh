namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IEncoder
    {
        EncodedData Encode<T>(T data);
    }
}