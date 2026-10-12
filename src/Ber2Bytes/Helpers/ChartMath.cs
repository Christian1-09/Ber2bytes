namespace Ber2Bytes.Helpers;

public static class ChartMath
{
    /// <summary>Rounds the chart's top value up to a "nice" number (1, 2, 2.5, 5 × 10ⁿ) so the axis labels look clean.</summary>
    public static decimal NiceMax(decimal max)
    {
        if (max <= 0) return 1;
        var exp = Math.Pow(10, Math.Floor(Math.Log10((double)max)));
        var f = (double)max / exp;
        double nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
        return (decimal)(nice * exp);
    }

    /// <summary>Axis label: 0, 500, 10k, 1.5M</summary>
    public static string Short(decimal v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000m).ToString("0.#", Fmt.PH) + "M",
        >= 1_000 => (v / 1_000m).ToString("0.#", Fmt.PH) + "k",
        _ => v.ToString("0", Fmt.PH)
    };
}
