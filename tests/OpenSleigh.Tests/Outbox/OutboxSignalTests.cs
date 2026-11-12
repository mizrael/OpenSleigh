using OpenSleigh.Outbox;

namespace OpenSleigh.Tests.Outbox;

public class OutboxSignalTests
{
    private static readonly TimeSpan Immediately = TimeSpan.Zero;

    [Fact]
    public async Task WaitForPendingMessagesAsync_should_time_out_when_nothing_was_signalled()
    {
        var sut = new OutboxSignal();

        Assert.False(await sut.WaitForPendingMessagesAsync(TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task WaitForPendingMessagesAsync_should_complete_when_signalled_while_waiting()
    {
        var sut = new OutboxSignal();

        var waiting = sut.WaitForPendingMessagesAsync(TimeSpan.FromSeconds(30)).AsTask();
        sut.NotifyPendingMessages();

        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Signals_raised_from_many_threads_should_be_cleared_by_one_call()
    {
        var sut = new OutboxSignal();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                sut.NotifyPendingMessages();
        })));

        Assert.True(await sut.WaitForPendingMessagesAsync(Immediately));
        sut.ClearPendingMessages();
        Assert.False(await sut.WaitForPendingMessagesAsync(Immediately));
    }

    [Fact]
    public async Task Signal_raised_after_a_clear_should_be_kept()
    {
        var sut = new OutboxSignal();
        sut.NotifyPendingMessages();
        sut.ClearPendingMessages();

        sut.NotifyPendingMessages();

        Assert.True(await sut.WaitForPendingMessagesAsync(Immediately));
    }

    [Fact]
    public async Task WaitForPendingMessagesAsync_should_observe_cancellation()
    {
        var sut = new OutboxSignal();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await sut.WaitForPendingMessagesAsync(TimeSpan.FromSeconds(30), cancellation.Token));
    }
}
