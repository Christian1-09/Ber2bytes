using System.Net;
using System.Text;
using Ber2Bytes.Data.Entities;

namespace Ber2Bytes.Helpers;

/// <summary>Builds the receipt as HTML. The same HTML is shown on screen and sent to the printer.</summary>
public static class ReceiptBuilder
{
    /// <summary>CSS used when printing (58mm / 80mm thermal printers or normal paper).</summary>
    public const string PrintCss = """
        @page { margin: 4mm; }
        body { margin: 0; }
        .receipt { font-family: Consolas, 'Courier New', monospace; font-size: 12px; width: 72mm; margin: 0 auto; color: #000; line-height: 1.45; }
        .rc-c { text-align: center; } .rc-store { font-size: 14px; font-weight: 700; }
        .rc-line { border-top: 1px dashed #000; margin: 6px 0; }
        .rc-row { display: flex; justify-content: space-between; gap: 6px; }
        .rc-item { font-weight: 700; margin-top: 3px; }
        .rc-total { font-size: 15px; font-weight: 800; }
        .rc-void { text-align: center; font-weight: 800; border: 2px solid #000; margin: 4px 0; }
        """;

    public static string Build(Sale s, StoreSettings st, string? reprintBy = null)
    {
        static string E(string? x) => WebUtility.HtmlEncode(x ?? "");
        var sb = new StringBuilder();
        sb.Append("<div class=\"receipt\">");
        sb.Append($"<div class=\"rc-c\"><div class=\"rc-store\">{E(st.StoreName)}</div>{E(st.BranchName)}");
        if (!string.IsNullOrWhiteSpace(st.Address)) sb.Append($"<br>{E(st.Address)}");
        if (!string.IsNullOrWhiteSpace(st.Phone)) sb.Append($"<br>Tel: {E(st.Phone)}");
        sb.Append("</div>");
        if (s.Status == SaleStatus.Voided) sb.Append("<div class=\"rc-void\">*** VOIDED ***</div>");
        sb.Append("<div class=\"rc-line\"></div>");
        Row(sb, "<b>SALES RECEIPT</b>", $"<b>{E(s.InvoiceNo)}</b>");
        Row(sb, "Date", E(s.Date.ToString("MMM d, yyyy h:mm tt", Fmt.PH)));
        Row(sb, "Cashier", E(s.Cashier?.FullName));
        Row(sb, "Customer", E(s.CustomerName));
        if (!string.IsNullOrWhiteSpace(s.CustomerPhone)) Row(sb, "Phone", E(s.CustomerPhone));
        sb.Append("<div class=\"rc-line\"></div>");

        foreach (var i in s.Items)
        {
            sb.Append($"<div class=\"rc-item\">{E(i.ProductName)}</div>");
            Row(sb, $"&nbsp;&nbsp;{i.Quantity} x {Fmt.Peso(i.UnitPrice)}", Fmt.Peso(i.LineTotal));
        }

        sb.Append("<div class=\"rc-line\"></div>");
        Row(sb, $"Subtotal ({s.Items.Sum(x => x.Quantity)} item/s)", Fmt.Peso(s.Subtotal));
        if (s.Discount > 0) Row(sb, "Discount", "-" + Fmt.Peso(s.Discount));
        sb.Append($"<div class=\"rc-row rc-total\"><span>TOTAL</span><span>{Fmt.Peso(s.Total)}</span></div>");
        if (s.PaymentMethod == PaymentMethod.Cash)
        {
            Row(sb, "Cash", Fmt.Peso(s.AmountTendered));
            Row(sb, "Change", Fmt.Peso(s.Change));
        }
        else
        {
            Row(sb, "GCash", Fmt.Peso(s.AmountTendered));
            Row(sb, "Ref No.", E(s.ReferenceNo));
        }
        sb.Append("<div class=\"rc-line\"></div>");
        sb.Append($"<div class=\"rc-c\">{E(st.ReceiptFooter)}</div>");
        sb.Append($"<div class=\"rc-c\" style=\"font-size:10px;margin-top:6px\">Returns accepted within {st.ReturnWindowDays} day(s) with this receipt.</div>");
        if (reprintBy != null) sb.Append($"<div class=\"rc-c\" style=\"font-size:10px\">REPRINT · {E(reprintBy)} · {DateTime.Now:MMM d, yyyy h:mm tt}</div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string left, string right) =>
        sb.Append($"<div class=\"rc-row\"><span>{left}</span><span>{right}</span></div>");
}
