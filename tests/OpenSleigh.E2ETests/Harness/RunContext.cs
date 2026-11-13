using Microsoft.Extensions.Configuration;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// what a store or a transport is given when a scenario starts: an id that no other run has,
/// to name its database, queues and topics after, and the settings of the suite.
/// </summary>
public sealed record RunContext(string RunId, IConfiguration Configuration)
{
    private const string RunIdPrefix = "e2e";
    private const int RunIdRandomLength = 12;

    private static readonly Lazy<IConfiguration> Settings = new(() => new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build());

    // short, lowercase, letters and digits only: it ends up in database, queue and topic names
    public static RunContext Create()
        => new($"{RunIdPrefix}{Guid.NewGuid().ToString("N")[..RunIdRandomLength]}", Settings.Value);

    /// <exception cref="InvalidOperationException">the connection string is not configured.</exception>
    public string GetRequiredConnectionString(string name)
    {
        var connectionString = Configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"the connection string '{name}' is not configured.");

        return connectionString;
    }
}
