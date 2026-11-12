using BenchmarkDotNet.Running;

// run everything:      dotnet run -c Release
// run one class:       dotnet run -c Release -- --filter *HotPathBenchmarks*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args.Length > 0 ? args : ["--filter", "*"]);
