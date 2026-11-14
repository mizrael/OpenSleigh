using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// the connections the hosts of one scenario have to RabbitMQ, found by the name the scenario gave them,
/// and closed through the management API of the broker, as a network that drops them would.
/// The connections of the other runs, on the same broker, are left alone.
/// </summary>
public sealed class RabbitMqConnections : IAsyncDisposable
{
    private const string SettingsSection = "Rabbit";
    private const int DefaultManagementPort = 15672;
    private const string CloseReasonHeader = "X-Reason";
    private const string CloseReason = "closed by an E2E scenario, on purpose";
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);

    private readonly string _connectionName;
    private readonly HttpClient _management;
    private readonly ConnectionFactory _connectionFactory;
    private readonly string _virtualHost;
    private IConnection? _connection;

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

        _connectionFactory = new ConnectionFactory
        {
            HostName = hostName,
            UserName = userName,
            Password = password,
            VirtualHost = _virtualHost,
            Port = AmqpTcpEndpoint.UseDefaultPort,
        };
    }

    /// <param name="connectionName">the name given to the hosts with <see cref="RabbitMqConnectionExtensions.NameRabbitMqConnections"/>.</param>
    public static RabbitMqConnections Named(string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        return new RabbitMqConnections(connectionName, RunContext.Create().Configuration.GetSection(SettingsSection));
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

    /// <summary>closes the connections, and returns once the broker lists none of them any more.</summary>
    public async Task CloseAsync(IReadOnlyCollection<string> connections, CancellationToken cancellationToken)
    {
        foreach (var connection in connections)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"connections/{Uri.EscapeDataString(connection)}");
            request.Headers.Add(CloseReasonHeader, CloseReason);

            // a connection that went away by itself in the meantime is not there to be closed
            using var response = await _management.SendAsync(request, cancellationToken);
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                response.EnsureSuccessStatusCode();
        }

        while ((await ListAsync(cancellationToken)).Intersect(connections).Any())
            await Task.Delay(PollingInterval, cancellationToken);
    }

    /// <summary>how many consumers a queue has. Null when the queue does not exist.</summary>
    public async Task<uint?> CountConsumersAsync(string queueName, CancellationToken cancellationToken)
    {
        // a connection of its own, which has no name and is not among the ones that are closed.
        // One channel per question: the broker closes a channel that asks for a queue that does not exist
        _connection ??= await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        try
        {
            return (await channel.QueueDeclarePassiveAsync(queueName, cancellationToken)).ConsumerCount;
        }
        catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == Constants.NotFound)
        {
            return null;
        }
    }

    /// <summary>returns once every queue has that many consumers.</summary>
    public async Task WaitForConsumersAsync(IReadOnlyCollection<string> queueNames, uint consumersOfEach, CancellationToken cancellationToken)
    {
        foreach (var queueName in queueNames)
        {
            while ((await CountConsumersAsync(queueName, cancellationToken) ?? 0) < consumersOfEach)
                await Task.Delay(PollingInterval, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _management.Dispose();

        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}

public static class RabbitMqConnectionExtensions
{
    /// <summary>
    /// gives the connections a host opens to RabbitMQ a name, for <see cref="RabbitMqConnections"/> to find them by.
    /// Call it from <see cref="HostSetup.ConfigureServices"/>, on a combination that uses RabbitMQ.
    /// </summary>
    public static IServiceCollection NameRabbitMqConnections(this IServiceCollection services, string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        return services.Decorate<IConnectionFactory>((_, factory) =>
        {
            factory.ClientProvidedName = connectionName;
            return factory;
        });
    }
}
