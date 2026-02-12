```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7781)
12th Gen Intel Core i7-1265U, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2


```
| Method               | Mean        | Error      | StdDev      | Median      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------- |------------:|-----------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| SagaInstance_Dynamic | 398.9376 ns | 30.2533 ns |  86.8023 ns | 380.2075 ns | 0.952 |    0.32 | 0.2050 | 0.0005 |    1288 B |        1.00 |
| SagaInstance_Wrapper | 403.4731 ns | 36.0994 ns | 102.4079 ns | 367.1741 ns | 0.963 |    0.35 | 0.2050 |      - |    1288 B |        1.00 |
| SagaInstance_Direct  | 452.2120 ns | 47.9645 ns | 138.3886 ns | 403.1934 ns | 1.080 |    0.43 | 0.2050 |      - |    1288 B |        1.00 |
| StateExtract_Dynamic |  10.5872 ns |  0.9976 ns |   2.8623 ns |   9.3178 ns | 0.025 |    0.01 |      - |      - |         - |        0.00 |
| StateExtract_Wrapper |  11.0169 ns |  0.8391 ns |   2.3666 ns |  10.0000 ns | 0.026 |    0.01 |      - |      - |         - |        0.00 |
| StateExtract_Direct  |   0.4598 ns |  0.1438 ns |   0.4171 ns |   0.2929 ns | 0.001 |    0.00 |      - |      - |         - |        0.00 |
