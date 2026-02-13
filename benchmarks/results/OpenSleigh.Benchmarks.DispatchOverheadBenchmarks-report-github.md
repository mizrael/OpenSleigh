```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7781)
12th Gen Intel Core i7-1265U, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2


```
| Method  | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------- |----------:|----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Direct  | 0.4055 ns | 0.2329 ns | 0.6568 ns | 0.0386 ns |     ? |       ? |         - |           ? |
| Dynamic | 7.8328 ns | 0.2277 ns | 0.6535 ns | 7.6225 ns |     ? |       ? |         - |           ? |
| Wrapper | 3.8480 ns | 0.2683 ns | 0.7867 ns | 3.5256 ns |     ? |       ? |         - |           ? |
