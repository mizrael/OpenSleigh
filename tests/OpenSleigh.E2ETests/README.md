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
| `Harness/FaultInjection/` | `Decorate<TService>()`, `Intercept(...)`, and the place for decorators shared by several scenarios |
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

`summary.txt` lists the failures per test, the tests that were not run (skipped by a known defect or by
`E2E_COMBINATIONS`) and how many runs could not clean up after themselves. A run in which no test passed is a failed run.

## Adding a scenario

One file in `Scenarios/`, with one class named like the file:

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

`RunScenarioAsync` starts `hostsCount` identical hosts and runs the scenario with a timeout. Then it ends the scenario
the way every scenario ends:

1. the hosts are left running for an observation window of 3 seconds, so that a message delivered twice, or one that
   should not be delivered, shows up;
2. the outbox must be empty (the check waits for the relay, which removes a message after publishing it);
3. nothing may be dead-lettered, unless the scenario says what it expects (`assertDeadLetters`);
4. the hosts must stop cleanly.

Rules that keep the suite stable:

- **Wait for what you expect, never for a time.** Sagas report through callbacks registered in `ConfigureServices`;
  an `Expectation` counts them and completes when the expected number is reached. A scenario that works ends as soon
  as that happens; the timeouts are spent only by one that fails.
- **Do not assert inside a handler.** A failed assertion there only makes the message fail. Use
  `Expectation.Require(condition, problem)` and assert `Problems` is empty at the end.
- **Read the outcome from the store** when the scenario is about state: `run.WaitForSagaAsync<TSaga>(correlationId, predicate, token)`.
- **Correlation ids are GUIDs**: the MongoDB store requires it.
- No retries around a scenario, no skipped tests. A case that cannot pass because of a product defect gets an entry in `KnownDefects.cs`.

`[MatrixData]` options:

| Option | Effect |
|---|---|
| `Only = [Combinations.PostgreSqlRabbitMq, ...]` | the combinations the scenario runs on. A name that is not a combination fails the test |
| `HostsCounts = [2, 5]` | replaces 1, 2 and 5. In memory a scenario runs with one host, or not at all when 1 is not listed |
| `RequiresSharedInfrastructure = true` | for hosts that have to reach each other, such as two services: not run in memory |

Do not use `Only` to leave out a combination on which the scenario fails: that is what `KnownDefects.cs` is for.
A scenario whose matrix has no case fails. One whose cases are all excluded, by a known defect or by
`E2E_COMBINATIONS`, is reported as skipped with the reason.

### When a case fails because of a product defect

Report the defect, then add an entry to `KnownDefects.Exclusions` with an id, the reason and the cases it affects:
those cases are no longer run, and a scenario left with no case at all is reported as skipped with the defect as
the reason. Deleting the entry puts them back; `E2E_INCLUDE_KNOWN_DEFECTS=true` runs them without
deleting it. An entry that excludes nothing fails `HarnessTests`. Entries match on the name of the scenario class,
which `[MatrixData]` takes from the name of its file.

### Scenarios that need more than identical hosts

The other `RunScenarioAsync` takes a scenario that starts its own hosts. It ends with the same four checks:

```csharp
await RunScenarioAsync(combination, async (run, cancellationToken) =>
{
    var api = await run.StartHostAsync(new HostSetup(bus => bus.SetPublishOnly()) { Service = "api" });
    var workers = await run.StartHostsAsync(3, workerSetup);

    await api.PublishAsync(new StartMySaga(), cancellationToken);
    await firstStepHandled.Reached.WaitAsync(cancellationToken);

    await workers[0].StopAsync();                            // a host leaves
    var replacement = await run.StartHostAsync(workerSetup); // another joins

    await completed.Reached.WaitAsync(cancellationToken);
});
```

A host must still be running when the scenario returns: the checks read the store through it.

| Need | Use |
|---|---|
| hosts with different roles | one `HostSetup` per role. Hosts with the same `Service` compete for messages (same queues, same consumer group); each service gets its own copy |
| stop, replace or add a host | `host.StopAsync()`, `run.StartHostAsync(setup)` |
| publish from a given host | `host.PublishAsync(message, token)` |
| final state, processed messages | `run.WaitForSagaAsync`, `run.FindSagaAsync`: read through a running host that has the saga |
| outbox | `run.CountOutboxMessagesAsync` |
| dead letters | `run.ReadDeadLettersAsync` (count per message type), on every combination |
| why a message was retried or dead-lettered | `run.ProcessingFailures`: every exception a host gave back to its transport, with `IsSagaNotFound`, `IsLockConflict` and `IsHandlerFailure` |
| dead letters that are expected | pass `assertDeadLetters` to `RunScenarioAsync`, for example `run => AssertOnlySagaNotFoundDeadLettersAsync(run, types...)` |
| hosts that start on a database nobody prepared | `prepareStore: false` on `RunScenarioAsync`: the first hosts create the database themselves, together |
| anything else in a host | `host.Services` |

### Making a service fail on purpose

In `HostSetup.ConfigureServices`, which runs after the library registered its services:

```csharp
ConfigureServices = services => services
    .Decorate<IPublisher>((provider, inner) => new FailingPublisher(inner))
    .Intercept(_ => new SlowCommit())
```

- `Decorate<TService>` replaces the last registration of a service with a wrapper. The service it wraps stays in
  the container, with its lifetime, and is disposed with it.
- `Intercept` is for the saga state repository and the outbox repository. The library asks those two whether they
  are also an `ISagaStateCommitter` or an `IOutboxBatchRepository`, and takes another path when they are not: a
  wrapper that implements only the main interface would change the behaviour under test. Derive from
  `SagaStateRepositoryInterceptor` or `OutboxRepositoryInterceptor`, override the calls to interfere with and call
  the base to go on; the harness keeps the other interface exactly when the original has it.
  `Decorate` refuses a wrapper that would hide one.

## Adding a store or a transport

One class in `Harness/Stores/` implementing `IStore`, or in `Harness/Transports/` implementing `ITransport`, one
line in `Combinations` (`Harness/Combination.cs`) and a constant there for each new combination: it is then combined
with every transport, or every store, and every scenario runs on the new combinations. A store or transport instance
lives for one scenario run: it names what it creates after `RunContext.RunId`, and removes it when disposed.
`SqlStore` is the base for a relational store.

Do not catch what removing things throws: the run gives every clean-up a bounded time and writes what goes wrong to
the output of the test, as a line starting with `CLEAN-UP FAILED`. A clean-up that fails never fails the test, and
never replaces what the scenario failed with.

Then give the `e2e` job its container (`.circleci/config.yml`), add it to `tests/infra/docker-compose.yml`, its
connection string to `appsettings.json` and its port to `run-e2e.sh`.

## Behaviour that is by design, and how scenarios assert it

**A lock conflict is retried, anything else is dead-lettered**, on every transport. A message retried by one
service goes back to that service only. The retry delay is 500 ms in these tests, on RabbitMQ and Kafka alike
(100 ms in memory, its default).

**A message for a saga that is not stored is dead-lettered at once, and never retried.** This holds for a saga
that does not exist and for one that is only late. The late case: a message that starts several sagas is handled by
one saga after the other, each stored on its own. What the first saga publishes can reach another host before the
next saga is stored; that host handles it for the first saga, fails for the next one with
`unable to locate state for Saga '...'`, and the message goes to the dead letter queue. It needs two hosts or more.

A scenario where this can happen does not ignore the dead letter queue: it names the message types that can arrive
early and passes `run => AssertOnlySagaNotFoundDeadLettersAsync(run, types...)` as `assertDeadLetters`, which
requires one dead letter for each such failure and nothing else, so any other dead letter still fails the test.
`MultipleSagasSameMessagesScenario` is the example. A scenario about a message for a saga that does not exist
asserts the same thing.

## What the harness does on purpose

- The SQL database and schema, and the MongoDB collections and indexes, are created by the first host before any
  host starts, and Kafka topics before their consumers. Hosts starting together on an empty database is a scenario
  of its own: `prepareStore: false`.
- `StartHostsAsync` returns once every running host receives its share of the messages, not merely once the hosts
  are started: a host subscribes in the background.
  - RabbitMQ: a message published before the queue of a service is bound never reaches it, and the hosts of a
    service share its queues. The harness waits until each queue has a consumer for every host.
  - Kafka: the hosts of a service are one consumer group, and a topic and its retry topic have 8 partitions
    (`KafkaTransport.Partitions`), so that each host is given some. The harness waits until the group is stable
    and every host has partitions; this takes a few seconds per group. A service cannot have more hosts than that.
    Messages of one type are spread over the partitions by their id, so messages for one saga can be consumed by
    different hosts at the same time.
- RabbitMQ queues and exchanges do not delete themselves: a queue that went away with the last host of its service
  would take its messages with it. The transport deletes them at the end of the run.
- Every run has its own database, queues, topics and client group, so tests can run at the same time.
  Scenario classes run in parallel (`xunit.runner.json`); the cases of one class run one after the other.

## Timeouts and infrastructure failures

**Timeouts.** A saga publishes its own timeout with `Publish(message, delay)` and handles it like any other message.
A scenario asserts a lower bound only (never handled before the delay), reads the time before the saga publishes the
timeout, and waits for the timeout through a callback. A timeout that must *not* be handled, because its saga
completed, reaches no handler: `services.ObserveDeliveriesOf<TMessage>(expectation)` tells when a host is done with it.
The in-memory outbox count leaves out a message that is not due, so "the outbox is empty" proves nothing there.

**Failures injected in a host**, on every combination. A `Fault` is turned on and off by the scenario, which waits
for it to have been hit (`WaitForHitsAsync`) rather than for a time:

| In `ConfigureServices` | While the fault is on |
|---|---|
| `FailPublishingWhile(fault)` | the relay cannot publish |
| `FailOutboxReadsWhile(fault)`, `FailOutboxDeletesWhile(fault)` | the relay cannot read its outbox, or remove what it published |
| `FailCommitsThatPublish<TMessage>(fault)` | a saga that published a `TMessage` cannot be committed |
| `ExpireOutboxClaimsAfter(duration)` | not a fault: on MongoDB a message the relay could not publish or remove is read again only when its claim expires, a minute by default |

**Connections taken away for real**, with what the services offer and for one run only:

| Helper | What it does | Combinations |
|---|---|---|
| `DatabaseOutage.Of(run)` | `DropConnectionsAsync`, `BeginAsync`, `EndAsync`: PostgreSQL terminates the sessions of the database of the run and refuses new ones; SQL Server sets it offline and online | `postgresql-*`, `sqlserver-*` |
| `RabbitMqConnections.Named(name)` | lists and closes the connections of the hosts through the management API; the hosts are given the name with `NameRabbitMqConnections(name)` | `*-rabbitmq` |

What these scenarios assert, because it is how the library behaves today:

- a message that fails for another reason than a lock is dead-lettered at once, also when the reason is the database:
  an interruption can cost messages. The scenarios require every message to be accounted for (completed, or in the
  dead letters with the failure a host reported), and the hosts to work again without a restart;
- a scenario cannot know which pooled connection a host will use next, so it does not expect a given message to
  succeed after an interruption: it issues work one item at a time until several in a row complete
  (`DatabaseInterruptionScenarioBase`);
- a saga that could not be released, because the database was away, stays locked until `LockMaxDuration`.

Kafka has no such switch: its interruptions need a proxy between the hosts and the broker, which the suite does not have.
