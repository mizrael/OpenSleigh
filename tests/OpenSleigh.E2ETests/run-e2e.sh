#!/usr/bin/env bash
# runs the E2E suite E2E_REPETITIONS times, keeps one trx file per run and a summary of the
# tests that failed, and exits non-zero when any run failed or ran no test.
# Expects the E2E project to be built already for TARGET_FRAMEWORK.
# E2E_COMBINATIONS (for example "inmemory" or "postgresql-rabbitmq,mongodb-kafka") runs only those combinations,
# E2E_FILTER narrows the tests, E2E_INCLUDE_KNOWN_DEFECTS=true runs the cases excluded in Harness/KnownDefects.cs.
set -u

repetitions="${E2E_REPETITIONS:-1}"
framework="${TARGET_FRAMEWORK:-net10.0}"
results_dir="${E2E_RESULTS_DIR:-/tmp/e2e-results}"
filter="${E2E_FILTER:-Category=E2E}"
project="$(dirname "$0")/OpenSleigh.E2ETests.csproj"
summary="$results_dir/summary.txt"

case "$repetitions" in
  ''|*[!0-9]*|0)
    echo "E2E_REPETITIONS must be a number of runs, 1 or more: '$repetitions'" >&2
    exit 2
    ;;
esac

mkdir -p "$results_dir"
# what an earlier invocation left behind would be counted as runs of this one
rm -f "$results_dir"/run-*.trx "$summary"

wait_for_port() {
  local host="$1" port="$2" attempts=60
  until (exec 3<>"/dev/tcp/$host/$port") 2>/dev/null; do
    attempts=$((attempts - 1))
    if [ "$attempts" -le 0 ]; then
      echo "$host:$port is not reachable" >&2
      return 1
    fi
    sleep 1
  done
}

kafka="${ConnectionStrings__kafka:-127.0.0.1:9092}"

# a service is waited for when a combination that uses it is run: all of them, unless E2E_COMBINATIONS names a few.
# The tests take a blank value as "not set": so does this script
export E2E_COMBINATIONS="$(echo "${E2E_COMBINATIONS:-}" | tr -d '[:space:]')"
uses() {
  [ -z "$E2E_COMBINATIONS" ] || [[ "$E2E_COMBINATIONS" == *"$1"* ]]
}

if uses sqlserver; then wait_for_port 127.0.0.1 1433 || exit 1; fi
if uses postgresql; then wait_for_port 127.0.0.1 5432 || exit 1; fi
if uses mongodb; then wait_for_port 127.0.0.1 27017 || exit 1; fi
if uses rabbitmq; then wait_for_port 127.0.0.1 5672 || exit 1; fi
if uses kafka; then wait_for_port "${kafka%%:*}" "${kafka##*:}" || exit 1; fi

failed_runs=0
for run in $(seq 1 "$repetitions"); do
  echo "=== E2E run $run of $repetitions ==="
  started=$(date +%s)
  if ! dotnet test "$project" --no-build --framework "$framework" --filter "$filter" \
      --logger "trx;LogFileName=run-$run.trx" --results-directory "$results_dir" \
      --blame-hang --blame-hang-timeout 15m --blame-hang-dump-type none \
      -- RunConfiguration.TreatNoTestsAsError=true; then
    failed_runs=$((failed_runs + 1))
  elif ! grep -q '<UnitTestResult [^>]*outcome="Passed"' "$results_dir/run-$run.trx" 2>/dev/null; then
    # tests that are all skipped do not fail dotnet test: a run in which nothing passed is not a green run
    echo "run $run: no test passed" | tee -a "$summary"
    failed_runs=$((failed_runs + 1))
  fi
  echo "run $run took $(( $(date +%s) - started ))s" | tee -a "$summary"
done

{
  echo
  echo "runs: $repetitions, failed runs: $failed_runs"
  echo
  echo "failures per test (out of $repetitions runs):"
  grep -ho '<UnitTestResult [^>]*outcome="Failed"' "$results_dir"/run-*.trx 2>/dev/null \
    | sed -E 's/.*testName="([^"]*)".*/\1/' | sort | uniq -c | sort -rn
  echo
  echo "tests not run (out of $repetitions runs):"
  grep -ho '<UnitTestResult [^>]*outcome="NotExecuted"' "$results_dir"/run-*.trx 2>/dev/null \
    | sed -E 's/.*testName="([^"]*)".*/\1/' | sort | uniq -c | sort -rn
  echo
  echo "runs of a scenario that could not be cleaned up: $(cat "$results_dir"/run-*.trx 2>/dev/null | grep -c 'CLEAN-UP FAILED')"
} | tee -a "$summary"

[ "$failed_runs" -eq 0 ]
