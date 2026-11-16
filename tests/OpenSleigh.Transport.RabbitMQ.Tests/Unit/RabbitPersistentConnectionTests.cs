using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

public class RabbitPersistentConnectionTests
{
    private readonly IConnectionFactory _factory = Substitute.For<IConnectionFactory>();
    private readonly IConnection _connection = Substitute.For<IConnection>();
    private readonly List<(LogLevel Level, string Text)> _log = [];

    public RabbitPersistentConnectionTests()
    {
        _connection.IsOpen.Returns(true);
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(_connection);
    }

    private RabbitPersistentConnection CreateSut()
    {
        var logger = Substitute.For<ILogger<RabbitPersistentConnection>>();
        logger.IsEnabled(default).ReturnsForAnyArgs(true);
        logger.WhenForAnyArgs(l => l.Log<object>(default, default, default!, default, default!))
              .Do(call => _log.Add((call.Arg<LogLevel>(), call.Args()[2]!.ToString()!)));

        return new RabbitPersistentConnection(_factory, logger);
    }

    private void LoseConnection(ShutdownInitiator initiator = ShutdownInitiator.Peer, ushort replyCode = Constants.ConnectionForced)
    {
        _connection.IsOpen.Returns(false);
        _connection.ConnectionShutdownAsync += Raise.Event<AsyncEventHandler<ShutdownEventArgs>>(
            _connection, new ShutdownEventArgs(initiator, replyCode, "closed on purpose"));
    }

    // as the client does it: the connection is open again before its channels are recovered
    private void ReopenConnection() => _connection.IsOpen.Returns(true);

    private void CompleteRecovery()
        => _connection.RecoverySucceededAsync += Raise.Event<AsyncEventHandler<AsyncEventArgs>>(_connection, new AsyncEventArgs());

    private Task OpenedConnections(int expected)
        => _factory.Received(expected).CreateConnectionAsync(Arg.Any<CancellationToken>());

    private Task CreatedChannels(int expected)
        => _connection.Received(expected).CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task CreateChannelAsync_should_open_one_connection_for_every_channel()
    {
        using var sut = CreateSut();

        await sut.CreateChannelAsync();
        await sut.CreateChannelAsync();

        await OpenedConnections(1);
        await CreatedChannels(2);
    }

    [Fact]
    public async Task A_connection_that_is_lost_should_not_be_replaced_and_should_be_reported_as_recovered_by_the_client()
    {
        using var sut = CreateSut();
        await sut.CreateChannelAsync();

        LoseConnection();

        Assert.False(sut.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateChannelAsync());
        await OpenedConnections(1);

        var (level, text) = Assert.Single(_log);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("closed on purpose", text);
    }

    [Theory]
    [InlineData(ShutdownInitiator.Peer, Constants.AccessRefused)]
    [InlineData(ShutdownInitiator.Application, Constants.ReplySuccess)]
    public async Task A_connection_the_client_does_not_recover_should_be_reported_as_an_error_that_needs_a_restart(
        ShutdownInitiator initiator, ushort replyCode)
    {
        using var sut = CreateSut();
        await sut.CreateChannelAsync();

        LoseConnection(initiator, replyCode);

        var (level, text) = Assert.Single(_log);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("restarted", text);
        await OpenedConnections(1);
    }

    [Fact]
    public async Task No_channel_should_be_created_until_the_client_has_recovered_the_channels_of_the_connection()
    {
        using var sut = CreateSut();
        await sut.CreateChannelAsync();
        LoseConnection();

        ReopenConnection();

        Assert.False(sut.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateChannelAsync());

        CompleteRecovery();

        Assert.True(sut.IsConnected);
        await sut.CreateChannelAsync();
        await OpenedConnections(1);
        await CreatedChannels(2);
        Assert.Equal(LogLevel.Information, _log.Last().Level);
    }

    [Fact]
    public async Task An_attempt_to_recover_the_connection_that_fails_should_be_reported()
    {
        using var sut = CreateSut();
        await sut.CreateChannelAsync();
        LoseConnection();

        _connection.ConnectionRecoveryErrorAsync += Raise.Event<AsyncEventHandler<ConnectionRecoveryErrorEventArgs>>(
            _connection, new ConnectionRecoveryErrorEventArgs(new InvalidOperationException("the broker is away")));

        var (level, text) = _log.Last();
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("the broker is away", text);
    }

    [Fact]
    public async Task Dispose_should_close_the_connection_once_and_report_nothing_and_open_no_other()
    {
        var sut = CreateSut();
        await sut.CreateChannelAsync();

        // the connection tells that it is closed while it is being disposed
        _connection.When(connection => connection.Dispose()).Do(_ => LoseConnection(ShutdownInitiator.Application));

        sut.Dispose();
        sut.Dispose();

        _connection.Received(1).Dispose();
        Assert.False(sut.IsConnected);
        Assert.Empty(_log);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.CreateChannelAsync());
        await OpenedConnections(1);
    }

    [Fact]
    public async Task A_connection_that_was_being_opened_when_the_owner_was_disposed_should_be_closed_once()
    {
        var opening = new TaskCompletionSource<IConnection>();
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(opening.Task);
        var sut = CreateSut();
        var creating = sut.CreateChannelAsync();

        sut.Dispose();
        opening.SetResult(_connection);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => creating);
        _connection.Received(1).Dispose();
        await CreatedChannels(0);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task A_connection_that_cannot_be_opened_should_not_be_tried_again_once_the_owner_is_disposed()
    {
        var sut = CreateSut();
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            sut.Dispose();
            return Task.FromException<IConnection>(new InvalidOperationException("the broker is away"));
        });

        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.CreateChannelAsync());

        await OpenedConnections(1);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task A_caller_that_gives_up_should_be_told_so_and_not_reported_as_a_failure()
    {
        using var givingUp = new CancellationTokenSource();
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            givingUp.Cancel();
            return Task.FromCanceled<IConnection>(call.Arg<CancellationToken>());
        });
        using var sut = CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.CreateChannelAsync(givingUp.Token));

        await OpenedConnections(1);
        Assert.Empty(_log);
    }
}
