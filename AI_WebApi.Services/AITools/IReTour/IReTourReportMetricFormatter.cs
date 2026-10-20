using System.Globalization;

namespace AI_WebApi.Services.AITools.IReTour;

internal static class IReTourReportMetricFormatter
{
    // The IReTour registry normalizes rate metrics to the range [0, 1].
    // Use the same scale for values, chart ticks and chart unit labels.
    public static bool IsRate(string code) =>
        code.EndsWith("_rate", StringComparison.Ordinal);

    public static string Unit(string code, string? unit) =>
        IsRate(code) ? "%" : unit ?? string.Empty;

    public static string Number(string code, decimal value) =>
        IsRate(code)
            ? (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Value(string code, decimal value, string? unit)
    {
        var number = Number(code, value);
        return IsRate(code) || string.IsNullOrWhiteSpace(unit)
            ? number
            : $"{number} {unit}";
    }
}
