namespace OpenSleigh.Transport
{
    public interface ISubscriber
    {
        ValueTask StartAsync(CancellationToken cancellationToken = default);
        ValueTask StopAsync(CancellationToken cancellationToken = default);
    }
}