#!/usr/bin/env bash
# runs the E2E suite E2E_REPETITIONS times, keeps one trx file per run and a summary of the
# tests that failed, and exits non-zero when any run failed.
# Expects the E2E project to be built already for TARGET_FRAMEWORK.
set -u

repetitions="${E2E_REPETITIONS:-1}"
framework="${TARGET_FRAMEWORK:-net10.0}"
results_dir="${E2E_RESULTS_DIR:-/tmp/e2e-results}"
filter="${E2E_FILTER:-Category=E2E}"
project="$(dirname "$0")/OpenSleigh.E2ETests.csproj"
summary="$results_dir/summary.txt"

mkdir -p "$results_dir"

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

wait_for_port 127.0.0.1 5432 || exit 1
wait_for_port 127.0.0.1 5672 || exit 1
wait_for_port 127.0.0.1 9092 || exit 1

failed_runs=0
for run in $(seq 1 "$repetitions"); do
  echo "=== E2E run $run of $repetitions ==="
  started=$(date +%s)
  if ! dotnet test "$project" --no-build --framework "$framework" --filter "$filter" \
      --logger "trx;LogFileName=run-$run.trx" --results-directory "$results_dir" \
      --blame-hang --blame-hang-timeout 15m --blame-hang-dump-type none; then
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
} | tee -a "$summary"

[ "$failed_runs" -eq 0 ]
