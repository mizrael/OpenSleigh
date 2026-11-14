using System.Text.Json;

namespace OpenSleigh.Benchmarks.Publish;

/// <summary>
/// What the benchmark gate calls noise, read from its settings: the page describes a change the way the gate judges it.
/// </summary>
internal sealed record GateSettings(
    double DefaultTolerance,
    IReadOnlyDictionary<string, double> Tolerances,
    IReadOnlyDictionary<string, double> MinimumDifferences)
{
    private const double FallbackTolerance = 0.30;

    public static GateSettings Read(string path)
    {
        var tolerances = new Dictionary<string, double>();
        var minimumDifferences = new Dictionary<string, double>();
        if (!File.Exists(path))
            return new GateSettings(FallbackTolerance, tolerances, minimumDifferences);

        using var gate = JsonDocument.Parse(File.ReadAllText(path));
        var defaultTolerance = gate.RootElement.TryGetProperty("defaultTolerance", out var value) ? value.GetDouble() : FallbackTolerance;

        if (gate.RootElement.TryGetProperty("scenarios", out var scenarios) && scenarios.ValueKind == JsonValueKind.Object)
            foreach (var scenario in scenarios.EnumerateObject())
            {
                if (scenario.Value.TryGetProperty("tolerance", out var tolerance))
                    tolerances[scenario.Name] = tolerance.GetDouble();
                if (scenario.Value.TryGetProperty("minimumDifference", out var minimumDifference))
                    minimumDifferences[scenario.Name] = minimumDifference.GetDouble();
            }

        return new GateSettings(defaultTolerance, tolerances, minimumDifferences);
    }

    public double ToleranceFor(string scenario) => Tolerances.GetValueOrDefault(scenario, DefaultTolerance);

    /// <summary>
    /// true when the change is inside the tolerance, or beyond it by less than the absolute floor of the scenario.
    /// </summary>
    public bool IsNoise(string scenario, double change, double? difference)
        => Math.Abs(change) <= ToleranceFor(scenario)
           || (difference is not null
               && MinimumDifferences.TryGetValue(scenario, out var floor)
               && Math.Abs(difference.Value) < floor);
}
