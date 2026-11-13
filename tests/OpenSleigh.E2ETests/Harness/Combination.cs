using OpenSleigh.E2ETests.Harness.Stores;
using OpenSleigh.E2ETests.Harness.Transports;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// a store and a transport the scenarios run on.
/// </summary>
/// <param name="HostsShareInfrastructure">false when every host has a store and a transport of its own,
/// as in memory: a second host would take no part in the scenario.</param>
public sealed record Combination(
    string Name,
    bool HostsShareInfrastructure,
    Func<RunContext, IStore> CreateStore,
    Func<RunContext, ITransport> CreateTransport)
{
    public override string ToString() => Name;
}

/// <summary>
/// the matrix: every store with every transport, plus in-memory.
/// A new store or transport is one class and one line here.
/// </summary>
public static class Combinations
{
    public const string InMemory = "inmemory";
    public const string PostgreSql = "postgresql";
    public const string SqlServer = "sqlserver";
    public const string MongoDb = "mongodb";
    public const string RabbitMq = "rabbitmq";
    public const string Kafka = "kafka";

    private const char Separator = '-';

    private static readonly IReadOnlyList<(string Name, Func<RunContext, IStore> Create)> Stores =
    [
        (SqlServer, context => new SqlServerStore(context)),
        (PostgreSql, context => new PostgreSqlStore(context)),
        (MongoDb, context => new MongoStore(context)),
    ];

    private static readonly IReadOnlyList<(string Name, Func<RunContext, ITransport> Create)> Transports =
    [
        (RabbitMq, context => new RabbitMqTransport(context)),
        (Kafka, context => new KafkaTransport(context)),
    ];

    public static readonly IReadOnlyList<Combination> All =
    [
        new Combination(InMemory, HostsShareInfrastructure: false, _ => new InMemoryStore(), _ => new InMemoryTransport()),
        .. from store in Stores
           from transport in Transports
           select new Combination(NameOf(store.Name, transport.Name), HostsShareInfrastructure: true, store.Create, transport.Create)
    ];

    public static string NameOf(string store, string transport) => $"{store}{Separator}{transport}";

    /// <summary>true when the combination uses the store or the transport of that name.</summary>
    public static bool Uses(string combination, string storeOrTransport)
        => combination.Split(Separator).Contains(storeOrTransport);

    /// <exception cref="ArgumentException">there is no combination of that name.</exception>
    public static Combination Get(string name)
        => All.FirstOrDefault(combination => combination.Name == name)
           ?? throw new ArgumentException(
               $"unknown combination '{name}'. Known ones: {string.Join(", ", All.Select(c => c.Name))}", nameof(name));
}
