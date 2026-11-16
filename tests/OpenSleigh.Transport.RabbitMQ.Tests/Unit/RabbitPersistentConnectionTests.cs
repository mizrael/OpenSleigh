using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

public class RabbitPersistentConnectionTests
{
    private readonly IConnectionFactory _factory = Substitute.For<IConnectionFactory>();
    private readonly IConnection _connection = Substitute.For<IConnection>();

    public RabbitPersistentConnectionTests()
    {
        _connection.IsOpen.Returns(true);
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(_connection);
    }

    private RabbitPersistentConnection CreateSut()
        => new(_factory, NullLogger<RabbitPersistentConnection>.Instance);

    private void LoseConnection(ShutdownInitiator initiator)
    {
        _connection.IsOpen.Returns(false);
        _connection.ConnectionShutdownAsync += Raise.Event<AsyncEventHandler<ShutdownEventArgs>>(
            _connection, new ShutdownEventArgs(initiator, Constants.ConnectionForced, "closed"));
    }

    private Task OpenedConnections(int expected)
        => _factory.Received(expected).CreateConnectionAsync(Arg.Any<CancellationToken>());

    [Fact]
    public async Task CreateChannelAsync_should_open_one_connection_for_every_channel()
    {
        using var sut = CreateSut();

        await sut.CreateChannelAsync();
        await sut.CreateChannelAsync();

        await OpenedConnections(1);
        await _connection.Received(2).CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task a_connection_that_is_lost_should_not_be_replaced_by_another_one()
    {
        using var sut = CreateSut();
        await sut.CreateChannelAsync();

        LoseConnection(ShutdownInitiator.Peer);

        Assert.False(sut.IsConnected);
        await OpenedConnections(1);
    }

    [Fact]
    public async Task CreateChannelAsync_should_fail_without_opening_a_connection_while_the_lost_one_is_being_recovered()
    {
        using var sut = CreateSut();
        await sut.CreateChannelAsync();
        LoseConnection(ShutdownInitiator.Peer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateChannelAsync());

        await OpenedConnections(1);
    }

    [Fact]
    public async Task CreateChannelAsync_should_use_the_same_connection_once_it_is_recovered()
    {
        using var sut = CreateSut();
        await sut.CreateChannelAsync();
        LoseConnection(ShutdownInitiator.Peer);

        _connection.IsOpen.Returns(true);
        await sut.CreateChannelAsync();

        Assert.True(sut.IsConnected);
        await OpenedConnections(1);
        await _connection.Received(2).CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispose_should_close_the_connection_and_open_no_other()
    {
        var sut = CreateSut();
        await sut.CreateChannelAsync();

        sut.Dispose();
        LoseConnection(ShutdownInitiator.Application);

        _connection.Received(1).Dispose();
        Assert.False(sut.IsConnected);
        await OpenedConnections(1);
    }

    [Fact]
    public async Task CreateChannelAsync_should_open_no_connection_once_disposed()
    {
        var sut = CreateSut();
        sut.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.CreateChannelAsync());

        await OpenedConnections(0);
    }

    [Fact]
    public async Task a_connection_that_was_being_opened_when_the_owner_was_disposed_should_be_closed()
    {
        var opening = new TaskCompletionSource<IConnection>();
        _factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(opening.Task);
        var sut = CreateSut();
        var creating = sut.CreateChannelAsync();

        sut.Dispose();
        opening.SetResult(_connection);

        await Assert.ThrowsAnyAsync<Exception>(() => creating);
        _connection.Received(1).Dispose();
        await _connection.DidNotReceive().CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
    }
}
