# Benchmarks

Two projects, measuring different things.

## OpenSleigh.Benchmarks (BenchmarkDotNet)

Micro-benchmarks for code that runs once per message.

```
cd benchmarks/OpenSleigh.Benchmarks
dotnet run -c Release                                   # everything
dotnet run -c Release -- --filter *HotPathBenchmarks*   # one class
```

## OpenSleigh.Benchmarks.Throughput

Drives the outbox relay, the consumers, the saga state repositories and whole sagas through the public API,
and reports throughput (higher is better) or time (lower is better).

```
cd benchmarks/OpenSleigh.Benchmarks.Throughput
dotnet run -c Release -- list                              # scenarios and what they do
dotnet run -c Release -- run --suite local --runs 5        # no infrastructure: fakes, in-memory, SQLite
dotnet run -c Release -- run --suite gate                  # real services, the pull request subset
dotnet run -c Release -- run --suite full                  # real services, everything
dotnet run -c Release -- run --scenario kafka-consume,rabbit-consume --runs 3 --out results/run.json
dotnet run -c Release -- profile --suite local             # where the time of an end-to-end saga goes, call by call
```

`profile` runs the end-to-end scenarios with the saga state repository, the outbox repository, the publisher and
the transaction manager wrapped, and prints calls and time per call. With the `full` suite the gate job prints it
for both sides and stores it with the results (`profile-base.md`, `profile-head.md`).

The `gate` and `full` suites need the services of `tests/infra/docker-compose.yml`. Connection settings default to
those containers and can be overridden with `BENCH_SQLSERVER`, `BENCH_POSTGRES`, `BENCH_MONGO`, `BENCH_KAFKA`,
`BENCH_RABBIT_HOST`, `BENCH_RABBIT_VHOST`, `BENCH_RABBIT_USER` and `BENCH_RABBIT_PASSWORD`.

## The benchmark gate

The `benchmark_gate` CircleCI job runs on every branch except the base branch (`develop`). It checks out the base
branch next to the pushed commit, builds the harness of the pushed commit against both source trees, and runs the
`gate` suite for both in interleaved rounds, on the same runner and the same service containers
(`benchmarks/run-gate.sh`). The job fails when a scenario is slower on the branch than on the base by more than
its tolerance, or when a scenario that works on the base fails on the branch.

- Each round runs base and branch back to back; the verdict uses the median, over the rounds, of the difference
  within each round. Absolute numbers move a lot between runners; the difference within a round moves much less.
- `benchmarks/gate.json` holds the tolerance (30% by default, per-scenario overrides), the minimum number of
  rounds, and lets a scenario be reported without gating (`"gate": false`).
- The harness may only use public API that exists on both sides. A scenario for something that only exists on the
  branch is compiled conditionally (see `RABBIT_CONSUMER_TUNING` in the project file) and reported as inconclusive.
- Put `[bench-full]` in the message of the last commit to run the `full` suite instead, or
  `[bench-scenarios name1,name2]` to run only those scenarios.
- Pipeline parameters: `benchmark_base_branch`, `benchmark_rounds`, `benchmark_report_only`.
- Results are printed in the job log and stored as artifacts (`benchmark-summary.md`, `benchmark-results/`).

To run the gate outside CI, with the services up and a checkout of the base branch:

```
BENCH_BASE_DIR=/path/to/base/checkout bash benchmarks/run-gate.sh
```

## Runs of the base branch, and the docs site

Every commit that lands on `develop` is measured too, by the `benchmark_develop` job: same script, `full` suite,
compared with the previous commit of `develop` (its first parent) in the same job. It only reports: a timing
difference never fails it, because nothing can be done about a commit that is already on `develop` and a noisy
runner would otherwise turn the branch red.

The results are published on the [Benchmarks page](https://mizrael.github.io/OpenSleigh/benchmarks.html) of the
docs site:

- `OpenSleigh.Benchmarks.Publish` finds the `benchmark_develop` jobs of recent `develop` commits through the commit
  statuses CircleCI reports to GitHub, downloads their artifacts through CircleCI's public API, and writes
  `docs/_includes/benchmark-results.md`, which `docs/benchmarks.md` includes. No CircleCI token is involved.
- The docs workflow (`.github/workflows/docs.yml`) runs it before every Jekyll build, and is triggered by the
  commit status of a finished `benchmark_develop` job, so the page is rebuilt after every run.
- Nothing is committed back to the repository, so a run cannot trigger another run. The file in the repository is
  a placeholder; it is what the page shows when no run can be read.
- CircleCI keeps artifacts for a limited time (30 days by default). The page shows up to 8 runs from the last 30
  commits that still have artifacts.

To preview the page content locally:

```
dotnet run --project benchmarks/OpenSleigh.Benchmarks.Publish -c Release
```
