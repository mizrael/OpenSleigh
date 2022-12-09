namespace OpenSleigh.Messaging
{
    public interface ISubscriber
    {
        ValueTask StartAsync(CancellationToken cancellationToken = default);
        ValueTask StopAsync(CancellationToken cancellationToken = default);
    }
}