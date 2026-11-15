using OpenSleigh.Outbox;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// the outbox relay of a host, with a pause: while paused the host relays nothing, as if it never got round to
/// its next pass. What it appends to the outbox stays there for the relay of another host.
/// </summary>
public sealed class PausableRelay : OutboxRepositoryInterceptor
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _isPaused;

    public PausableRelay(bool isPaused = false)
    {
        _isPaused = isPaused;
    }

    /// <summary>
    /// returns once no read is in flight: whatever is appended after that is not read by this host.
    /// </summary>
    public void Pause()
    {
        _gate.Wait();
        try
        {
            _isPaused = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(IOutboxRepository repository, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _isPaused ? [] : await base.ReadPendingAsync(repository, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
