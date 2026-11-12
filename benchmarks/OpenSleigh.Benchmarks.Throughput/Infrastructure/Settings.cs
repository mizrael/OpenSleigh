namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

/// <summary>
/// Connection settings for the throwaway service containers. Defaults match tests/infra/docker-compose.yml
/// and .circleci/config.yml; each one can be overridden with an environment variable.
/// </summary>
internal sealed record Settings(
    string SqlServer,
    string Postgres,
    string Mongo,
    string Kafka,
    string RabbitHost,
    string RabbitVirtualHost,
    string RabbitUser,
    string RabbitPassword)
{
    public static Settings FromEnvironment() => new(
        SqlServer: Read("BENCH_SQLSERVER", "Server=127.0.0.1;Database={0};User=sa;Password=Sup3r_p4ssword123;Trust Server Certificate=true;"),
        Postgres: Read("BENCH_POSTGRES", "Host=localhost;Port=5432;Username=postgres;Password=Sup3r_p4ssword123;Database={0};"),
        Mongo: Read("BENCH_MONGO", "mongodb://127.0.0.1:27017"),
        Kafka: Read("BENCH_KAFKA", "127.0.0.1:9092"),
        RabbitHost: Read("BENCH_RABBIT_HOST", "127.0.0.1"),
        RabbitVirtualHost: Read("BENCH_RABBIT_VHOST", "/opensleigh-tests"),
        RabbitUser: Read("BENCH_RABBIT_USER", "guest"),
        RabbitPassword: Read("BENCH_RABBIT_PASSWORD", "guest"));

    public IEnumerable<(string Name, string Host, int Port)> Endpoints()
    {
        yield return ("sqlserver", "127.0.0.1", 1433);
        yield return ("postgres", "127.0.0.1", 5432);
        yield return ("mongo", "127.0.0.1", 27017);
        yield return ("rabbitmq", RabbitHost, 5672);

        var kafka = Kafka.Split(',')[0].Split(':');
        yield return ("kafka", kafka[0], kafka.Length > 1 ? int.Parse(kafka[1]) : 9092);
    }

    private static string Read(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
