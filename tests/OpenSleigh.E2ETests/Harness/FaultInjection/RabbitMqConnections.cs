using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// the connections the hosts of one scenario have to RabbitMQ, as the broker sees them: found by the name
/// of their <see cref="RabbitMqConnectionLog"/> and closed through the management API, as a network that drops them
/// would. The connections of the other runs, on the same broker, are left alone.
/// </summary>
public sealed class RabbitMqConnections : IDisposable
{
    private const string SettingsSection = "Rabbit";
    private const int DefaultManagementPort = 15672;
    private const string CloseReasonHeader = "X-Reason";
    private const string CloseReason = "closed by an E2E scenario, on purpose";
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);

    private readonly string _connectionName;
    private readonly HttpClient _management;
    private readonly string _virtualHost;

    private RabbitMqConnections(string connectionName, IConfiguration settings)
    {
        _connectionName = connectionName;
        _virtualHost = settings["VirtualHost"] ?? "/";

        var hostName = settings["HostName"] ?? throw new InvalidOperationException("the RabbitMQ host name is not configured.");
        var userName = settings["UserName"]!;
        var password = settings["Password"]!;
        var managementPort = settings.GetValue("ManagementPort", DefaultManagementPort);

        _management = new HttpClient { BaseAddress = new Uri($"http://{hostName}:{managementPort}/api/") };
        _management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));
    }

    /// <param name="log">the log the hosts were given with <see cref="RabbitMqConnectionLogExtensions.TrackRabbitMqConnections"/>.</param>
    public static RabbitMqConnections Of(RabbitMqConnectionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return new RabbitMqConnections(log.Name, RunContext.Create().Configuration.GetSection(SettingsSection));
    }

    /// <summary>the connections of the hosts that are open, as the broker names them.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _management.GetAsync($"vhosts/{Uri.EscapeDataString(_virtualHost)}/connections", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var connections = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return connections.RootElement.EnumerateArray()
            .Where(connection => ClientProvidedNameOf(connection) == _connectionName)
            .Select(connection => connection.GetProperty("name").GetString()!)
            .ToArray();
    }

    private static string? ClientProvidedNameOf(JsonElement connection)
        => connection.TryGetProperty("user_provided_name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    /// <summary>returns the connections of the hosts once the broker lists that many of them.</summary>
    public async Task<IReadOnlyList<string>> WaitForAsync(int atLeast, CancellationToken cancellationToken)
    {
        while (true)
        {
            var connections = await ListAsync(cancellationToken);
            if (connections.Count >= atLeast)
                return connections;

            await Task.Delay(PollingInterval, cancellationToken);
        }
    }

    /// <summary>
    /// asks the broker to close the connections, and returns when it has been asked: what the broker lists lags
    /// behind, see <see cref="WaitUntilClosedAsync"/>.
    /// </summary>
    public async Task CloseAsync(IReadOnlyCollection<string> connections, CancellationToken cancellationToken)
    {
        foreach (var connection in connections)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"connections/{Uri.EscapeDataString(connection)}");
            request.Headers.Add(CloseReasonHeader, CloseReason);

            // a connection that went away by itself in the meantime is not there to be closed
            using var response = await _management.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound)
                response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>returns once the broker lists none of the connections any more.</summary>
    public async Task WaitUntilClosedAsync(IReadOnlyCollection<string> connections, CancellationToken cancellationToken)
    {
        while ((await ListAsync(cancellationToken)).Intersect(connections).Any())
            await Task.Delay(PollingInterval, cancellationToken);
    }

    public void Dispose() => _management.Dispose();
}
