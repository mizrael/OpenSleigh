```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7781)
12th Gen Intel Core i7-1265U, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2


```
| Method  | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------- |----------:|----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Direct  |  92.93 ns |  4.260 ns | 12.153 ns |  88.36 ns |  1.01 |    0.18 | 0.1594 | 0.0004 |    1000 B |        1.00 |
| Dynamic | 123.78 ns | 13.171 ns | 36.933 ns | 113.18 ns |  1.35 |    0.43 | 0.1594 | 0.0004 |    1000 B |        1.00 |
| Wrapper |  85.91 ns |  2.811 ns |  8.064 ns |  83.28 ns |  0.94 |    0.14 | 0.1594 | 0.0004 |    1000 B |        1.00 |
