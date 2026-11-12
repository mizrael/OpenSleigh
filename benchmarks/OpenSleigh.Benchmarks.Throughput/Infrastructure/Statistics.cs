namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

internal static class Statistics
{
    public static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return double.NaN;

        var sorted = values.OrderBy(v => v).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>(max - min) / median: how far apart repeated runs of the same thing landed.</summary>
    public static double RelativeSpread(IReadOnlyCollection<double> values)
    {
        var median = Median(values);
        if (values.Count < 2 || double.IsNaN(median) || median == 0)
            return double.NaN;
        return (values.Max() - values.Min()) / median;
    }

    public static string Format(double value)
    {
        if (double.IsNaN(value))
            return "n/a";

        var magnitude = Math.Abs(value);
        var format = magnitude >= 100 ? "N0" : magnitude >= 10 ? "N1" : magnitude >= 1 ? "N2" : "N3";
        return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string FormatPercent(double fraction)
        => double.IsNaN(fraction) ? "n/a" : fraction.ToString("+0.0%;-0.0%", System.Globalization.CultureInfo.InvariantCulture);
}
