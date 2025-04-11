namespace OpenSleigh.Transport;

public interface IMessageSubscriber
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface IMessageSubscriber<TM> : IMessageSubscriber where TM : IMessage { }