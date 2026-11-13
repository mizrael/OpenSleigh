# OpenSleigh end-to-end tests

Whole sagas on real hosts, against real stores and transports. Every scenario runs on a matrix of combinations:

| Combination | Store | Transport | Hosts per scenario |
|---|---|---|---|
| `inmemory` | in-memory | in-memory | 1 (hosts share nothing) |
| `sqlserver-rabbitmq`, `sqlserver-kafka` | SQL Server | RabbitMQ, Kafka | 1, 2 and 5 |
| `postgresql-rabbitmq`, `postgresql-kafka` | PostgreSQL | RabbitMQ, Kafka | 1, 2 and 5 |
| `mongodb-rabbitmq`, `mongodb-kafka` | MongoDB | RabbitMQ, Kafka | 1, 2 and 5 |

A test is one cell: a scenario method, a combination, a number of hosts. Its name carries all three, for example
`SimpleSagaScenario.run_single_message_scenario(combination: "mongodb-kafka", hostsCount: 2)`.

## Layout

| Folder | What is in it |
|---|---|
| `Scenarios/` | one class per scenario |
| `Sagas/` | the sagas and messages the scenarios use |
| `Harness/` | what scenarios are built from: `ScenarioBase`, `ScenarioRun`, `ScenarioHost`, `HostSetup`, `Expectation`, `MatrixData` |
| `Harness/Stores/`, `Harness/Transports/` | one class per store and per transport |
| `Harness/FaultInjection/` | `Decorate<TService>()`, and the place for decorators shared by several scenarios |
| `Harness/KnownDefects.cs` | the only place where a case is excluded, each with its reason |

## Running

In CI the suite runs in the `e2e` job, through `run-e2e.sh`. Locally, from the repository root:

```bash
docker compose -f tests/infra/docker-compose.yml up -d
dotnet build tests/OpenSleigh.E2ETests --framework net10.0
bash tests/OpenSleigh.E2ETests/run-e2e.sh
```

| Variable | Effect |
|---|---|
| `E2E_COMBINATIONS` | comma separated combinations to run, for example `inmemory` (needs no container) or `postgresql-rabbitmq,mongodb-kafka` |
| `E2E_FILTER` | a `dotnet test` filter, `Category=E2E` by default. One scenario: `Category=E2E&FullyQualifiedName~ParentChildScenario` |
| `E2E_REPETITIONS` | how many times the suite runs; raise it to measure flakiness (the `e2e_repetitions` pipeline parameter in CI) |
| `E2E_INCLUDE_KNOWN_DEFECTS` | `true` runs the cases excluded in `KnownDefects.cs` as well, to check a fix |
| `E2E_RESULTS_DIR` | where the `run-N.trx` files and `summary.txt` go |

`dotnet test` works too: `E2E_COMBINATIONS=inmemory dotnet test tests/OpenSleigh.E2ETests --framework net10.0`.

## Adding a scenario

One file in `Scenarios/`:

```csharp
public class MyScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    [Theory]
    [MatrixData]
    public async Task my_scenario(string combination, int hostsCount)
    {
        var completed = new Expectation(1);
        Action<IMessageContext<MySagaCompleted>> onCompleted = _ => completed.Signal();

        var setup = new HostSetup(bus => bus.AddSaga<MySaga>())
        {
            ConfigureServices = services => services.AddSingleton(onCompleted)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishAsync(new StartMySaga(), cancellationToken);
            await completed.Reached.WaitAsync(cancellationToken);
        });

        Assert.Equal(1, completed.Count);
    }
}
```

`RunScenarioAsync` starts `hostsCount` identical hosts, runs the scenario with a timeout, keeps the hosts running for
an observation window of 3 seconds (so that a message delivered twice, or one that should not be delivered, shows up),
checks that the outbox is empty and that nothing was dead-lettered, and stops the hosts.

Rules that keep the suite stable:

- **Wait for what you expect, never for a time.** Sagas report through callbacks registered in `ConfigureServices`;
  an `Expectation` counts them and completes when the expected number is reached. A scenario that works ends as soon
  as that happens; the timeouts are spent only by one that fails.
- **Do not assert inside a handler.** A failed assertion there only makes the message fail. Use
  `Expectation.Require(condition, problem)` and assert `Problems` is empty at the end.
- **Read the outcome from the store** when the scenario is about state: `run.WaitForSagaAsync<TSaga>(correlationId, predicate, token)`.
- **Correlation ids are GUIDs**: the MongoDB store requires it.
- No retries around a scenario, no skipped tests. A case that cannot pass because of a product defect gets an entry in `KnownDefects.cs`.

`[MatrixData]` options: `Only = [...]` restricts the combinations, `HostsCounts = [...]` replaces 1, 2 and 5.
A scenario restricted to combinations that `E2E_COMBINATIONS` does not ask for is reported as skipped.

### When a case fails because of a product defect

Report the defect, then add an entry to `KnownDefects.Exclusions` with an id, the reason and the cases it affects:
those cases are no longer run. Deleting the entry puts them back; `E2E_INCLUDE_KNOWN_DEFECTS=true` runs them without
deleting it. An entry that excludes nothing fails `HarnessTests`.

### Scenarios that need more than identical hosts

Use the run directly instead of `RunScenarioAsync`:

```csharp
await using var run = CreateRun(combination);

var api = await run.StartHostAsync(new HostSetup(bus => bus.SetPublishOnly()) { Service = "api" });
var workers = await run.StartHostsAsync(3, workerSetup);

await RunWithTimeoutAsync(async cancellationToken =>
{
    await api.PublishAsync(new StartMySaga(), cancellationToken);
    await firstStepHandled.Reached.WaitAsync(cancellationToken);

    await workers[0].StopAsync();                       // a host leaves
    var replacement = await run.StartHostAsync(workerSetup); // another joins

    await completed.Reached.WaitAsync(cancellationToken);
});

await AssertNothingIsLeftBehindAsync(run);
await run.StopHostsAsync();
```

| Need | Use |
|---|---|
| hosts with different roles | one `HostSetup` per role. Hosts with the same `Service` compete for messages (same queues, same consumer group); each service gets its own copy |
| stop, replace or add a host | `host.StopAsync()`, `run.StartHostAsync(setup)` |
| publish from a given host | `host.PublishAsync(message, token)` |
| final state, processed messages | `run.WaitForSagaAsync`, `run.FindSagaAsync` |
| outbox drained | `run.CountOutboxMessagesAsync` |
| dead letters | `run.ReadDeadLettersAsync` (count per message type), where `run.SupportsDeadLetters` (not in memory) |
| why a message was retried or dead-lettered | `run.ProcessingFailures`: every exception a host gave back to its transport, with `IsSagaNotFound` and `IsLockConflict` |
| dead letters that are expected | pass `verify` to `RunScenarioAsync`, with `AssertOutboxIsEmptyAsync` and `AssertOnlySagaNotFoundDeadLettersAsync(run, types...)` |
| a service that fails on purpose | `services.Decorate<IPublisher>((provider, inner) => new FailingPublisher(inner))` in `ConfigureServices`, which runs after the library registered its services |
| anything else in a host | `host.Services` |

## Adding a store or a transport

One class in `Harness/Stores/` implementing `IStore`, or in `Harness/Transports/` implementing `ITransport`, and one
line in `Combinations` (`Harness/Combination.cs`): it is then combined with every transport, or every store, and
every scenario runs on the new combinations. A store or transport instance lives for one scenario run: it names what
it creates after `RunContext.RunId`, and removes it when disposed. `SqlStore` is the base for a relational store.

Then give the `e2e` job its container (`.circleci/config.yml`), add it to `tests/infra/docker-compose.yml`, its
connection string to `appsettings.json` and its port to `run-e2e.sh`.

## Behaviour that is by design, and how scenarios assert it

**A message for a saga that is not stored is dead-lettered at once, and never retried.** This holds for a saga
that does not exist and for one that is only late. The late case: a message that starts several sagas is handled by
one saga after the other, each stored on its own. What the first saga publishes can reach another host before the
next saga is stored; that host handles it for the first saga, fails for the next one with
`unable to locate state for Saga '...'`, and the message goes to the dead letter queue. It needs two hosts or more.

A scenario where this can happen does not ignore the dead letter queue: it names the message types that can arrive
early and calls `AssertOnlySagaNotFoundDeadLettersAsync(run, types...)`, which requires one dead letter for each
such failure and nothing else, so any other dead letter still fails the test. `MultipleSagasSameMessagesScenario`
is the example. A scenario about a message for a saga that does not exist asserts the same thing.

## What the harness does on purpose

- The SQL database and schema are created before the first host starts, and Kafka topics before their consumers:
  several hosts starting on an empty database, and consumers starting before their topics exist, are scenarios of their own.
- Every run has its own database, queues, topics and client group, so tests can run at the same time.
  Scenario classes run in parallel (`xunit.runner.json`); the cases of one class run one after the other.
