using System.Globalization;

namespace Ber2Bytes.Helpers;

/// <summary>Formatting helpers used across the UI, PDFs and Excel files.</summary>
public static class Fmt
{
    public static readonly CultureInfo PH = CultureInfo.GetCultureInfo("en-PH");

    public static string Peso(decimal value) => "₱" + value.ToString("N2", PH);

    /// <summary>Short peso for charts: ₱12.5k, ₱1.2M</summary>
    public static string PesoShort(decimal value) => value switch
    {
        >= 1_000_000 => "₱" + (value / 1_000_000m).ToString("0.#", PH) + "M",
        >= 1_000 => "₱" + (value / 1_000m).ToString("0.#", PH) + "k",
        _ => "₱" + value.ToString("0", PH)
    };

    public static string Number(int value) => value.ToString("N0", PH);
    public static string Date(DateTime value) => value.ToString("MMM d, yyyy", PH);
    public static string Date(DateTime? value) => value.HasValue ? Date(value.Value) : "—";
    public static string DateTimeText(DateTime value) => value.ToString("MMM d, yyyy h:mm tt", PH);
    public static string DateTimeText(DateTime? value) => value.HasValue ? DateTimeText(value.Value) : "—";
    public static string Time(DateTime value) => value.ToString("h:mm tt", PH);

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    /// <summary>Safe file name piece, e.g. "Sales Report" → "Sales_Report".</summary>
    public static string FileSafe(string text) =>
        string.Concat(text.Select(c => char.IsLetterOrDigit(c) ? c : '_')).Trim('_');
}
