using BenchmarkDotNet.Running;
using OpenSleigh.Benchmarks;

BenchmarkRunner.Run([
    typeof(DispatchOverheadBenchmarks),
    typeof(SagaInstanceCreationBenchmarks)
]);
