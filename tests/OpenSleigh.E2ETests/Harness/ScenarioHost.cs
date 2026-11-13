using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// one host of a scenario. It is started by its <see cref="ScenarioRun"/> and can be stopped on its own,
/// while the others keep running.
/// </summary>
public sealed class ScenarioHost
{
    private readonly IHost _host;
    private int _stopped;

    internal ScenarioHost(IHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public IServiceProvider Services => _host.Services;

    public string Service => Services.GetServiceName();

    public bool IsRunning { get; private set; }

    /// <summary>
    /// publishes a message the way an application does: through the bus of this host, into its outbox.
    /// </summary>
    public async Task<OutboxAppendResult> PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : IMessage
    {
        await using var scope = Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        return await bus.PublishAsync(message, cancellationToken);
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await _host.StartAsync(cancellationToken);
        IsRunning = true;
    }

    /// <summary>
    /// stops the host and disposes it, the way a deployment stops an instance. Does nothing the second time.
    /// </summary>
    /// <exception cref="AggregateException">the host did not stop cleanly.</exception>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        // the host is disposed whatever stopping it did: one left behind keeps consuming from queues and topics
        var failures = new List<Exception>();
        await TryAsync(() => _host.StopAsync(), failures);
        IsRunning = false;
        await TryAsync(DisposeHostAsync, failures);

        if (failures.Count > 0)
            throw new AggregateException("the host did not stop cleanly.", failures);
    }

    private async Task DisposeHostAsync()
    {
        if (_host is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            _host.Dispose();
    }

    private static async Task TryAsync(Func<Task> action, ICollection<Exception> failures)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            failures.Add(e);
        }
    }
}
