#!/usr/bin/env bash
#
# Benchmark gate: is this checkout slower than the base branch?
#
# The harness in this checkout is built twice, once against the base branch sources and once against
# this checkout's sources, and both builds run the same scenarios in interleaved rounds on the same
# machine against the same services. Comparing them round by round cancels most of what a shared CI
# runner does to absolute numbers.
#
# Required: BENCH_BASE_DIR   a checkout of the base branch
# Optional: BENCH_OUT_DIR, BENCH_SUITE, BENCH_SCENARIOS, BENCH_ROUNDS, BENCH_MIN_ROUNDS, BENCH_TIME_BUDGET_SECONDS,
#           BENCH_REPORT_ONLY=true to print the comparison without failing

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE_DIR="${BENCH_BASE_DIR:?BENCH_BASE_DIR must point to a checkout of the base branch}"
OUT_DIR="${BENCH_OUT_DIR:-$REPO_ROOT/benchmarks/artifacts}"
SUITE="${BENCH_SUITE:-gate}"
# a commit can ask for every real-infrastructure scenario instead of the pull request subset,
# or for specific scenarios: [bench-full] or [bench-scenarios name1,name2] in its message
HEAD_MESSAGE="$(git -C "$REPO_ROOT" log -1 --pretty=%B 2>/dev/null)"
if echo "$HEAD_MESSAGE" | grep -q '\[bench-full\]'; then SUITE="full"; fi
SCENARIOS="${BENCH_SCENARIOS:-$(echo "$HEAD_MESSAGE" | sed -n 's/.*\[bench-scenarios \([^]]*\)\].*/\1/p' | head -n 1)}"
if [ -n "$SCENARIOS" ]; then selection=(--scenario "$SCENARIOS"); else selection=(--suite "$SUITE"); fi
ROUNDS="${BENCH_ROUNDS:-5}"
MIN_ROUNDS="${BENCH_MIN_ROUNDS:-3}"
TIME_BUDGET_SECONDS="${BENCH_TIME_BUDGET_SECONDS:-600}"

PROJECT_DIR="$REPO_ROOT/benchmarks/OpenSleigh.Benchmarks.Throughput"
ASSEMBLY="OpenSleigh.Benchmarks.Throughput.dll"
RESULTS_DIR="$OUT_DIR/results"

build_variant() {
  local variant="$1" src_root="$2"
  rm -rf "$PROJECT_DIR/bin" "$PROJECT_DIR/obj"
  dotnet build "$PROJECT_DIR" -c Release -o "$OUT_DIR/bin/$variant" -p:OpenSleighSrcRoot="$src_root" --nologo -v q
}

mkdir -p "$RESULTS_DIR"

echo "== building the harness against this checkout (head)"
build_variant head "$REPO_ROOT/src" || { echo "the harness does not build against this checkout"; exit 1; }

echo "== building the harness against the base branch (base)"
build_variant base "$BASE_DIR/src" || { echo "the harness does not build against the base branch: it may only use public API that exists on both sides"; exit 1; }

echo "== waiting for the services"
dotnet "$OUT_DIR/bin/head/$ASSEMBLY" wait || exit 1

started=$SECONDS
for round in $(seq 1 "$ROUNDS"); do
  if [ "$round" -gt "$MIN_ROUNDS" ] && [ $((SECONDS - started)) -gt "$TIME_BUDGET_SECONDS" ]; then
    echo "== time budget of ${TIME_BUDGET_SECONDS}s used up after $((round - 1)) rounds"
    break
  fi

  # alternate which side goes first, so neither always runs on a warmer or busier machine
  if [ $((round % 2)) -eq 1 ]; then order="base head"; else order="head base"; fi

  for variant in $order; do
    echo "== round $round: $variant"
    dotnet "$OUT_DIR/bin/$variant/$ASSEMBLY" run "${selection[@]}" --variant "$variant" --round "$round" \
      --out "$RESULTS_DIR/$variant-r$round.json"
  done
done
echo "== measured for $((SECONDS - started))s"

# where the time of an end-to-end saga goes on each side, call by call: printed and stored, never judged
if [ "$SUITE" = "full" ] && [ -z "$SCENARIOS" ]; then
  for variant in base head; do
    echo "== profile: $variant"
    dotnet "$OUT_DIR/bin/$variant/$ASSEMBLY" profile --variant "$variant" --out "$RESULTS_DIR/profile-$variant.md" || true
  done
fi

compare_args=(--results "$RESULTS_DIR" --config "$REPO_ROOT/benchmarks/gate.json" --markdown "$OUT_DIR/summary.md")
if [ "${BENCH_REPORT_ONLY:-false}" = "true" ]; then compare_args+=(--no-fail); fi

dotnet "$OUT_DIR/bin/head/$ASSEMBLY" compare "${compare_args[@]}"
