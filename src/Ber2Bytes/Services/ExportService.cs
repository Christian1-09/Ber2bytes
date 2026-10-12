using Ber2Bytes.Data.Entities;
using Ber2Bytes.Helpers;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Ber2Bytes.Services;

/// <summary>Turns a <see cref="ReportResult"/> into a PDF (QuestPDF) or an Excel .xlsx file (ClosedXML).</summary>
public class ExportService(SettingsService settings, AuthService auth, FileDialogService files, AuditService audit)
{
    private const string Red = "#E11D2A";
    private const string Dark = "#111111";

    /// <summary>Asks where to save, writes the PDF and opens it (so it can be printed). Returns the path or null if cancelled.</summary>
    public async Task<string?> SavePdfAsync(ReportResult r)
    {
        var path = files.AskSavePath($"{Fmt.FileSafe(r.Title)}_{DateTime.Now:yyyy-MM-dd}.pdf", "PDF file (*.pdf)|*.pdf");
        if (path == null) return null;
        await Task.Run(() => WritePdf(r, path));
        await audit.LogAsync(auth.UserId, auth.UserName, "Export PDF", r.Title);
        files.OpenWithDefaultApp(path);
        return path;
    }

    public async Task<string?> SaveExcelAsync(ReportResult r)
    {
        var path = files.AskSavePath($"{Fmt.FileSafe(r.Title)}_{DateTime.Now:yyyy-MM-dd}.xlsx", "Excel workbook (*.xlsx)|*.xlsx");
        if (path == null) return null;
        await Task.Run(() => WriteExcel(r, path));
        await audit.LogAsync(auth.UserId, auth.UserName, "Export Excel", r.Title);
        files.ShowInFolder(path);
        return path;
    }

    public static string FormatCell(object? v, ColKind kind) => v switch
    {
        null => "",
        decimal d when kind == ColKind.Money => Fmt.Peso(d),
        decimal d => d.ToString("N2", Fmt.PH),
        int i => i.ToString("N0", Fmt.PH),
        DateTime dt when kind == ColKind.Date && dt.TimeOfDay == TimeSpan.Zero => Fmt.Date(dt),
        DateTime dt => Fmt.DateTimeText(dt),
        _ => v.ToString() ?? ""
    };

    private static bool IsRight(ColKind k) => k is ColKind.Money or ColKind.Number;

    // ───────────────────────────── PDF ─────────────────────────────
    public void WritePdf(ReportResult r, string path)
    {
        var s = settings.Current;
        var who = auth.UserName;
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(r.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily("Segoe UI").FontColor("#1F2937"));

                page.Header().PaddingBottom(10).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(s.StoreName.ToUpperInvariant()).FontSize(14).Bold().FontColor(Red);
                            c.Item().Text($"{s.BranchName}{(string.IsNullOrWhiteSpace(s.Address) ? "" : " · " + s.Address)}{(string.IsNullOrWhiteSpace(s.Phone) ? "" : " · " + s.Phone)}").FontSize(8).FontColor("#6B7280");
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text(r.Title).FontSize(13).Bold().FontColor(Dark);
                            c.Item().AlignRight().Text(r.Subtitle).FontSize(8).FontColor("#6B7280");
                            c.Item().AlignRight().Text($"Generated {DateTime.Now:MMM d, yyyy h:mm tt} by {who}").FontSize(8).FontColor("#6B7280");
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(2).LineColor(Red);
                });

                page.Content().Column(col =>
                {
                    if (r.Summary.Count > 0)
                    {
                        col.Item().PaddingBottom(10).Row(row =>
                        {
                            foreach (var (label, value) in r.Summary)
                                row.RelativeItem().Border(0.5f).BorderColor("#E5E7EB").Background("#F9FAFB").Padding(6).Column(c =>
                                {
                                    c.Item().Text(label).FontSize(7.5f).FontColor("#6B7280");
                                    c.Item().Text(value).FontSize(10).Bold();
                                });
                        });
                    }

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            foreach (var c in r.Columns) cd.RelativeColumn(c.Width);
                        });
                        table.Header(h =>
                        {
                            foreach (var c in r.Columns)
                            {
                                var cell = h.Cell().Background(Dark).PaddingVertical(5).PaddingHorizontal(4);
                                (IsRight(c.Kind) ? cell.AlignRight() : cell.AlignLeft())
                                    .Text(c.Header.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Colors.White);
                            }
                        });

                        if (r.Rows.Count == 0)
                            table.Cell().ColumnSpan((uint)r.Columns.Count).Padding(12).AlignCenter().Text("No records for the selected filters.").FontColor("#6B7280");

                        int n = 0;
                        foreach (var row in r.Rows)
                        {
                            var bg = n++ % 2 == 0 ? "#FFFFFF" : "#F9FAFB";
                            for (int i = 0; i < r.Columns.Count; i++)
                            {
                                var c = r.Columns[i];
                                var cell = table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#E5E7EB").PaddingVertical(4).PaddingHorizontal(4);
                                var text = FormatCell(i < row.Length ? row[i] : null, c.Kind);
                                var t = (IsRight(c.Kind) ? cell.AlignRight() : cell.AlignLeft()).Text(text);
                                if (c.Kind == ColKind.Status)
                                    t.Bold().FontColor(text.Contains("Out") || text.Contains("Dead") ? "#B91C1C" : "#B45309");
                            }
                        }

                        if (r.Totals != null && r.Rows.Count > 0)
                        {
                            for (int i = 0; i < r.Columns.Count; i++)
                            {
                                var c = r.Columns[i];
                                var cell = table.Cell().BorderTop(1.5f).BorderColor(Dark).Background("#FEF2F2").PaddingVertical(5).PaddingHorizontal(4);
                                (IsRight(c.Kind) ? cell.AlignRight() : cell.AlignLeft())
                                    .Text(FormatCell(i < r.Totals.Length ? r.Totals[i] : null, c.Kind)).Bold();
                            }
                        }
                    });

                    if (!string.IsNullOrEmpty(r.Note))
                        col.Item().PaddingTop(8).Text(r.Note).FontSize(8).Italic().FontColor("#6B7280");

                    if (r.SignatureLines)
                    {
                        col.Item().PaddingTop(40).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Prepared by:").FontSize(8).FontColor("#6B7280");
                                c.Item().PaddingTop(24).Width(200).LineHorizontal(0.75f);
                                c.Item().Text(who).Bold();
                            });
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("Received / Confirmed by (Supplier):").FontSize(8).FontColor("#6B7280");
                                c.Item().PaddingTop(24).Width(200).LineHorizontal(0.75f);
                                c.Item().Text("Name, signature & date").FontSize(8);
                            });
                        });
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"{s.StoreName} — Automated Inventory and Sales Tracking System").FontSize(7).FontColor("#9CA3AF");
                    row.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(7).FontColor("#9CA3AF"));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            });
        }).GeneratePdf(path);
    }

    // ───────────────────────────── Excel ─────────────────────────────
    public void WriteExcel(ReportResult r, string path)
    {
        var s = settings.Current;
        using var wb = new XLWorkbook();
        var name = new string(r.Title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        var ws = wb.Worksheets.Add(name.Length > 31 ? name[..31] : name);
        int cols = r.Columns.Count;

        ws.Cell(1, 1).Value = s.StoreName;
        ws.Range(1, 1, 1, cols).Merge().Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.FromHtml(Red));
        ws.Cell(2, 1).Value = r.Title;
        ws.Range(2, 1, 2, cols).Merge().Style.Font.SetBold().Font.SetFontSize(12);
        ws.Cell(3, 1).Value = r.Subtitle;
        ws.Range(3, 1, 3, cols).Merge();
        ws.Cell(4, 1).Value = $"Generated {DateTime.Now:MMM d, yyyy h:mm tt} by {auth.UserName}";
        ws.Range(4, 1, 4, cols).Merge().Style.Font.SetFontColor(XLColor.Gray);

        int row = 6;
        if (r.Summary.Count > 0)
        {
            foreach (var (label, value) in r.Summary)
            {
                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 1).Style.Font.SetFontColor(XLColor.Gray);
                ws.Cell(row, 2).Value = value;
                ws.Cell(row, 2).Style.Font.SetBold();
                row++;
            }
            row++;
        }

        int headerRow = row;
        for (int c = 0; c < cols; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = r.Columns[c].Header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml(Dark));
        }
        row++;

        foreach (var data in r.Rows)
        {
            for (int c = 0; c < cols; c++) SetCell(ws.Cell(row, c + 1), c < data.Length ? data[c] : null, r.Columns[c].Kind);
            row++;
        }

        if (r.Totals != null && r.Rows.Count > 0)
        {
            for (int c = 0; c < cols; c++)
            {
                SetCell(ws.Cell(row, c + 1), c < r.Totals.Length ? r.Totals[c] : null, r.Columns[c].Kind);
                ws.Cell(row, c + 1).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#FEF2F2"))
                    .Border.SetTopBorder(XLBorderStyleValues.Medium);
            }
            row++;
        }

        if (!string.IsNullOrEmpty(r.Note))
        {
            ws.Cell(row + 1, 1).Value = r.Note;
            ws.Cell(row + 1, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
        }

        if (r.Rows.Count > 0)
            ws.Range(headerRow, 1, headerRow + r.Rows.Count, cols).SetAutoFilter();
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns(1, cols).AdjustToContents(headerRow, row);
        foreach (var col in ws.Columns(1, cols)) if (col.Width > 60) col.Width = 60;
        wb.SaveAs(path);
    }

    private static void SetCell(IXLCell cell, object? v, ColKind kind)
    {
        switch (v)
        {
            case null: cell.Value = Blank.Value; break;
            case decimal d:
                cell.Value = (double)d;
                cell.Style.NumberFormat.Format = kind == ColKind.Money ? "\"₱\"#,##0.00" : "#,##0.00";
                break;
            case int i:
                cell.Value = i;
                cell.Style.NumberFormat.Format = "#,##0";
                break;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = dt.TimeOfDay == TimeSpan.Zero ? "mmm d, yyyy" : "mmm d, yyyy h:mm AM/PM";
                break;
            default:
                cell.Value = v.ToString();
                break;
        }
    }

    // ───────────────────────────── Product list ─────────────────────────────
    public static ReportResult ProductList(List<Product> products, bool showCost)
    {
        var r = new ReportResult
        {
            Title = "Product List", Subtitle = $"{products.Count} products as of {DateTime.Now:MMM d, yyyy}", Landscape = true,
            Columns = [new("SKU", ColKind.Mono, 1.2f), new("Barcode", ColKind.Mono, 1.2f), new("Product", ColKind.Text, 2.8f), new("Category", ColKind.Text, 1.2f),
                       new("Brand"), new("Supplier", ColKind.Text, 1.4f)]
        };
        if (showCost) r.Columns.Add(new("Cost", ColKind.Money));
        r.Columns.AddRange([new("Price", ColKind.Money), new("Stock", ColKind.Number, .6f), new("Reorder", ColKind.Number, .6f), new("Status", ColKind.Status, .9f)]);

        foreach (var p in products)
        {
            var status = p.Stock <= 0 ? "Out of Stock" : p.Stock <= p.ReorderLevel ? "Low Stock" : "In Stock";
            var row = new List<object?> { p.Sku, p.Barcode, p.Name, p.Category?.Name, p.Brand, p.Supplier?.Name };
            if (showCost) row.Add(p.CostPrice);
            row.AddRange([p.SellingPrice, p.Stock, p.ReorderLevel, status]);
            r.Rows.Add(row.ToArray());
        }
        return r;
    }
}
