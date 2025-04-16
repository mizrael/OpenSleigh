namespace OpenSleigh.Transport;

public interface IMessage { }

public interface IHasIdempotencyKey
{
    string IdempotencyKey { get; }
}