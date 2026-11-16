using Microsoft.Extensions.Logging.Abstractions;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public class RabbitPersistentConnectionTests : IClassFixture<RabbitFixture>
{
    private const int ManagementPort = 15672;
    private const int ConnectionLosses = 2;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(100);

    // long enough for a second connection to show up at the broker: the recovery interval, several times over
    private static readonly TimeSpan ObservationPeriod = TimeSpan.FromSeconds(3);

    private readonly RabbitFixture _fixture;

    public RabbitPersistentConnectionTests(RabbitFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task should_keep_one_connection_when_it_is_lost_and_none_when_disposed()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var connectionName = $"persistent-connection-{Guid.NewGuid():N}";
        using var broker = new BrokerConnections(_fixture.RabbitConfiguration, connectionName);

        var factory = _fixture.ConnectionFactory;
        factory.ClientProvidedName = connectionName;
        factory.AutomaticRecoveryEnabled = true;
        factory.NetworkRecoveryInterval = _fixture.RabbitConfiguration.RetryDelay;

        var sut = new RabbitPersistentConnection(factory, NullLogger<RabbitPersistentConnection>.Instance);
        try
        {
            await using var channel = await sut.CreateChannelAsync(timeout.Token);

            for (var loss = 1; loss <= ConnectionLosses; loss++)
            {
                var open = await broker.WaitForAsync(connections => connections.Count == 1, timeout.Token);
                await broker.CloseAsync(open, timeout.Token);

                await broker.WaitForAsync(connections => connections.Except(open).Any() && sut.IsConnected, timeout.Token);
                await broker.AssertStaysAsync(1, ObservationPeriod, timeout.Token);

                await using var channelAfterLoss = await sut.CreateChannelAsync(timeout.Token);
                Assert.True(channelAfterLoss.IsOpen);
            }
        }
        finally
        {
            sut.Dispose();
        }

        await broker.WaitForAsync(connections => connections.Count == 0, timeout.Token);
        await broker.AssertStaysAsync(0, ObservationPeriod, timeout.Token);
    }

    /// <summary>the connections with one client provided name, as the management API of the broker lists them.</summary>
    private sealed class BrokerConnections : IDisposable
    {
        private readonly HttpClient _management;
        private readonly string _virtualHost;
        private readonly string _connectionName;

        public BrokerConnections(RabbitConfiguration configuration, string connectionName)
        {
            _virtualHost = configuration.VirtualHost;
            _connectionName = connectionName;

            _management = new HttpClient { BaseAddress = new Uri($"http://{configuration.HostName}:{ManagementPort}/api/") };
            _management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{configuration.UserName}:{configuration.Password}")));
        }

        private async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
        {
            using var response = await _management.GetAsync($"vhosts/{Uri.EscapeDataString(_virtualHost)}/connections", cancellationToken);
            response.EnsureSuccessStatusCode();

            using var connections = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return connections.RootElement.EnumerateArray()
                .Where(connection => connection.TryGetProperty("user_provided_name", out var name)
                                     && name.ValueKind == JsonValueKind.String
                                     && name.GetString() == _connectionName)
                .Select(connection => connection.GetProperty("name").GetString()!)
                .ToArray();
        }

        public async Task<IReadOnlyList<string>> WaitForAsync(Func<IReadOnlyList<string>, bool> expected, CancellationToken cancellationToken)
        {
            var connections = await ListAsync(CancellationToken.None);
            while (!expected(connections))
            {
                Assert.False(cancellationToken.IsCancellationRequested,
                    $"the broker lists {connections.Count} connections named '{_connectionName}', which is not what was waited for.");

                await Task.Delay(PollingInterval, CancellationToken.None);
                connections = await ListAsync(CancellationToken.None);
            }

            return connections;
        }

        public async Task AssertStaysAsync(int expected, TimeSpan period, CancellationToken cancellationToken)
        {
            using var observing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            observing.CancelAfter(period);

            while (!observing.IsCancellationRequested)
            {
                Assert.Equal(expected, (await ListAsync(CancellationToken.None)).Count);
                await Task.Delay(PollingInterval, CancellationToken.None);
            }
        }

        public async Task CloseAsync(IEnumerable<string> connections, CancellationToken cancellationToken)
        {
            foreach (var connection in connections)
            {
                using var response = await _management.DeleteAsync($"connections/{Uri.EscapeDataString(connection)}", cancellationToken);
                if (response.StatusCode != HttpStatusCode.NotFound)
                    response.EnsureSuccessStatusCode();
            }
        }

        public void Dispose() => _management.Dispose();
    }
}
