using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Threading.Channels;

namespace OpenSleigh.InMemory.Messaging;

public class InMemorySubscriber : IMessageSubscriber, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelReader<MessageEnvelope> _reader;
    private readonly ILogger<InMemorySubscriber> _logger;
    private readonly IPublisher _publisher;
    private readonly InMemorySubscriberOptions _options;
    
    private CancellationTokenSource? _stoppingCts;
    private Task? _consumerTask;
    private bool disposedValue;

    /// <summary>
    /// creates a subscriber that processes every message in its own dependency injection scope,
    /// with its own <see cref="IMessageProcessor"/>: messages are processed several at a time,
    /// and what processes one of them is not built to be shared.
    /// </summary>
    public InMemorySubscriber(IServiceScopeFactory scopeFactory,
        ChannelReader<MessageEnvelope> reader,
        ILogger<InMemorySubscriber> logger,
        IPublisher publisher,
        InMemorySubscriberOptions? options = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options ?? InMemorySubscriberOptions.Defaults;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _consumerTask = Task.Run(async () => await ConsumeMessagesAsync(_stoppingCts.Token));

        return ValueTask.CompletedTask;
    }
   
    private async ValueTask ConsumeMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _reader.ReadMultipleAsync(_options.MaxMessagesBatchSize, cancellationToken)
                                     .ConfigureAwait(false);
            var tasks = batch.Select(async outboxMessage =>
            {
                try
                {
                    await ProcessInOwnScopeAsync(outboxMessage, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogError(e,
                        "an exception has occurred while processing message '{MessageId}': {Error}",
                        outboxMessage.MessageId,
                        e.Message);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    await _publisher.PublishAsync(outboxMessage, cancellationToken)
                                    .ConfigureAwait(false);
                }
            }).ToArray();

            await Task.WhenAll(tasks);
        }
    }

    private async ValueTask ProcessInOwnScopeAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var messageProcessor = scope.ServiceProvider.GetRequiredService<IMessageProcessor>();
        await messageProcessor.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _stoppingCts?.Cancel();
            _stoppingCts = null;
        }
        finally
        {
            if (_consumerTask is not null)
                await Task.WhenAny(_consumerTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            _consumerTask = null;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _stoppingCts?.Cancel();
                _stoppingCts?.Dispose();
                _stoppingCts = null;
                _consumerTask = null;
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}