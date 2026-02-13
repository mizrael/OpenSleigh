```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7781)
12th Gen Intel Core i7-1265U, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.12 (9.0.1225.60609), X64 RyuJIT AVX2


```
| Method               | Mean        | Error      | StdDev     | Median      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------- |------------:|-----------:|-----------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| SagaInstance_Direct  | 145.2719 ns | 19.7221 ns | 56.9028 ns | 122.8624 ns | 1.129 |    0.58 | 0.1594 | 0.0004 |    1000 B |        1.00 |
| SagaInstance_Dynamic | 223.3149 ns | 29.3239 ns | 86.4621 ns | 235.1741 ns | 1.736 |    0.88 | 0.1593 |      - |    1000 B |        1.00 |
| SagaInstance_Wrapper | 134.7151 ns | 21.8173 ns | 61.8920 ns | 110.7623 ns | 1.047 |    0.60 | 0.1593 | 0.0002 |    1000 B |        1.00 |
| StateExtract_Direct  |   0.5388 ns |  0.2345 ns |  0.6691 ns |   0.2991 ns | 0.004 |    0.01 |      - |      - |         - |        0.00 |
| StateExtract_Dynamic |  10.1973 ns |  0.5835 ns |  1.7204 ns |   9.5943 ns | 0.079 |    0.03 |      - |      - |         - |        0.00 |
| StateExtract_Wrapper |   2.2152 ns |  0.4858 ns |  1.3939 ns |   1.7580 ns | 0.017 |    0.01 |      - |      - |         - |        0.00 |
