using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

public enum ColKind { Text, Mono, Number, Money, Date, Status }
public record ReportColumn(string Header, ColKind Kind = ColKind.Text, float Width = 1);

/// <summary>A generic table report. The same object is shown on screen and exported to PDF and Excel.</summary>
public class ReportResult
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<ReportColumn> Columns { get; set; } = [];
    public List<object?[]> Rows { get; set; } = [];
    public object?[]? Totals { get; set; }
    public List<(string Label, string Value)> Summary { get; set; } = [];
    public string? Note { get; set; }
    /// <summary>Adds "Prepared by / Received by" signature lines (used for supplier lists).</summary>
    public bool SignatureLines { get; set; }
    public bool Landscape { get; set; }
}

public enum ReportType { Sales, TopProducts, SlowMoving, Valuation, LowStock }

public enum SlowFilter { All, DeadOnly, SlowOnly }

public class ReportOptions
{
    public DateTime From { get; set; } = DateTime.Today.AddDays(-29);
    public DateTime To { get; set; } = DateTime.Today;
    public int? CategoryId { get; set; }
    public int? SupplierId { get; set; }
    public bool NoSupplierOnly { get; set; }
    public int Days { get; set; } = 60;
    public SlowFilter Slow { get; set; } = SlowFilter.All;
}

public record SlowItem(Product Product, int SoldQty, DateTime? LastSold, int? DaysSinceLastSale, bool IsDead, decimal ValueAtCost);

public class ReportService(IDbContextFactory<AppDbContext> dbf, AuthService auth, SettingsService settings)
{
    public static readonly Dictionary<ReportType, (string Name, string Icon, string Description, string Perm)> Catalog = new()
    {
        [ReportType.Sales] = ("Sales Report", "receipt", "Transactions, discounts, refunds and gross profit for a date range", Perm.ReportBusiness),
        [ReportType.TopProducts] = ("Top-Selling Products", "trending", "Best sellers ranked by quantity sold and revenue", Perm.ReportBusiness),
        [ReportType.SlowMoving] = ("Slow-Moving & Dead Stock", "hourglass", "Products that are not selling and the money tied up in them", Perm.ReportBusiness),
        [ReportType.Valuation] = ("Inventory Valuation", "box", "Stock on hand valued at cost and at selling price", Perm.ReportBusiness),
        [ReportType.LowStock] = ("Low Stock / Reorder List", "alert", "Items at or below reorder level, grouped by supplier — send to suppliers", Perm.ReportLowStock),
    };

    public Task<ReportResult> BuildAsync(ReportType type, ReportOptions o) => type switch
    {
        ReportType.Sales => SalesAsync(o),
        ReportType.TopProducts => TopProductsAsync(o),
        ReportType.SlowMoving => SlowMovingReportAsync(o),
        ReportType.Valuation => ValuationAsync(o),
        _ => LowStockAsync(o)
    };

    private static string Range(ReportOptions o) => $"{o.From:MMM d, yyyy} – {o.To:MMM d, yyyy}";

    // ─────────────── Sales ───────────────
    private async Task<ReportResult> SalesAsync(ReportOptions o)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var from = o.From.Date; var to = o.To.Date.AddDays(1);
        var sales = await db.Sales.AsNoTracking().Include(s => s.Items).Include(s => s.Cashier)
            .Where(s => s.Status == SaleStatus.Completed && s.Date >= from && s.Date < to)
            .OrderBy(s => s.Date).ToListAsync();
        var refunds = await db.Returns.AsNoTracking()
            .Where(r => r.Status == ReturnStatus.Completed && r.ProcessedAt >= from && r.ProcessedAt < to)
            .Select(r => r.RefundAmount).ToListAsync();

        decimal Profit(Sale s) => s.Items.Sum(i => (i.UnitPrice - i.UnitCost) * i.Quantity) - s.Discount;
        var r = new ReportResult
        {
            Title = "Sales Report", Subtitle = Range(o), Landscape = true,
            Columns = [new("OR No.", ColKind.Mono), new("Date & Time", ColKind.Date, 1.4f), new("Customer", ColKind.Text, 1.4f),
                       new("Cashier"), new("Payment"), new("Items", ColKind.Number, .6f), new("Discount", ColKind.Money),
                       new("Total", ColKind.Money), new("Gross Profit", ColKind.Money)],
        };
        foreach (var s in sales)
            r.Rows.Add([s.InvoiceNo, s.Date, s.CustomerName, s.Cashier?.FullName, s.PaymentMethod.ToString(),
                        s.Items.Sum(i => i.Quantity), s.Discount, s.Total, Profit(s)]);

        var revenue = sales.Sum(s => s.Total);
        var profit = sales.Sum(s => Profit(s));
        var refundTotal = refunds.Sum();
        r.Totals = ["TOTAL", null, null, null, null, sales.Sum(s => s.Items.Sum(i => i.Quantity)), sales.Sum(s => s.Discount), revenue, profit];
        r.Summary =
        [
            ("Transactions", sales.Count.ToString("N0")),
            ("Gross Sales", Helpers.Fmt.Peso(revenue)),
            ("Refunds", Helpers.Fmt.Peso(refundTotal)),
            ("Net Sales", Helpers.Fmt.Peso(revenue - refundTotal)),
            ("Gross Profit", Helpers.Fmt.Peso(profit)),
            ("Cash / GCash", $"{Helpers.Fmt.Peso(sales.Where(s => s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.Total))} / {Helpers.Fmt.Peso(sales.Where(s => s.PaymentMethod == PaymentMethod.GCash).Sum(s => s.Total))}"),
        ];
        return r;
    }

    // ─────────────── Top products ───────────────
    public async Task<List<(int ProductId, string Name, string Sku, int Qty, decimal Revenue, decimal Profit)>> TopProductsDataAsync(DateTime from, DateTime toExclusive, int take = int.MaxValue)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var items = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale!.Status == SaleStatus.Completed && i.Sale.Date >= from && i.Sale.Date < toExclusive)
            .Select(i => new { i.ProductId, i.ProductName, i.Sku, Qty = i.Quantity - i.ReturnedQty, i.UnitPrice, i.UnitCost })
            .ToListAsync();
        return items.GroupBy(i => i.ProductId)
            .Select(g => (g.Key, g.First().ProductName, g.First().Sku, g.Sum(x => x.Qty),
                          g.Sum(x => x.UnitPrice * x.Qty), g.Sum(x => (x.UnitPrice - x.UnitCost) * x.Qty)))
            .Where(x => x.Item4 > 0)
            .OrderByDescending(x => x.Item4).ThenByDescending(x => x.Item5)
            .Take(take).ToList();
    }

    private async Task<ReportResult> TopProductsAsync(ReportOptions o)
    {
        var data = await TopProductsDataAsync(o.From.Date, o.To.Date.AddDays(1));
        await using var db = await dbf.CreateDbContextAsync();
        var cats = await db.Products.AsNoTracking().Include(p => p.Category).ToDictionaryAsync(p => p.Id, p => p.Category!.Name);
        var r = new ReportResult
        {
            Title = "Top-Selling Products", Subtitle = Range(o),
            Columns = [new("#", ColKind.Number, .4f), new("SKU", ColKind.Mono, 1.2f), new("Product", ColKind.Text, 2.6f), new("Category", ColKind.Text, 1.2f),
                       new("Qty Sold", ColKind.Number, .8f), new("Revenue", ColKind.Money), new("Gross Profit", ColKind.Money)]
        };
        int rank = 1;
        foreach (var d in data)
            r.Rows.Add([rank++, d.Sku, d.Name, cats.GetValueOrDefault(d.ProductId, "—"), d.Qty, d.Revenue, d.Profit]);
        r.Totals = [null, null, "TOTAL", null, data.Sum(d => d.Qty), data.Sum(d => d.Revenue), data.Sum(d => d.Profit)];
        return r;
    }

    // ─────────────── Slow-moving & dead stock ───────────────
    /// <summary>
    /// Looks at products that still have stock:
    ///  • Dead stock  = no sales at all in the last <paramref name="days"/> days.
    ///  • Slow-moving = sold only a few units (≤ setting) in that period.
    /// </summary>
    public async Task<List<SlowItem>> SlowMovingDataAsync(int days)
    {
        var maxQty = settings.Current.SlowMovingMaxQty;
        var since = DateTime.Today.AddDays(-days);
        await using var db = await dbf.CreateDbContextAsync();
        var products = await db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Supplier)
            .Where(p => !p.IsArchived && p.Stock > 0).ToListAsync();
        // Read the sold lines once, then group in memory (simple and fast enough for a store).
        var lines = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale!.Status == SaleStatus.Completed)
            .Select(i => new { i.ProductId, i.Sale!.Date, Qty = i.Quantity - i.ReturnedQty })
            .ToListAsync();
        var stats = lines.GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Last = g.Max(i => i.Date), Qty = g.Where(i => i.Date >= since).Sum(i => i.Qty) })
            .ToList();
        var map = stats.ToDictionary(s => s.ProductId);

        var list = new List<SlowItem>();
        foreach (var p in products)
        {
            map.TryGetValue(p.Id, out var s);
            int qty = s?.Qty ?? 0;
            DateTime? last = s?.Last;
            // Products added recently are not "dead" yet.
            if (p.CreatedAt > since && last == null) continue;
            if (qty > maxQty) continue;
            int? daysSince = last.HasValue ? (int)(DateTime.Today - last.Value.Date).TotalDays : null;
            list.Add(new SlowItem(p, qty, last, daysSince, qty == 0, p.Stock * p.CostPrice));
        }
        return list.OrderByDescending(x => x.IsDead).ThenByDescending(x => x.DaysSinceLastSale ?? int.MaxValue)
                   .ThenByDescending(x => x.ValueAtCost).ToList();
    }

    private async Task<ReportResult> SlowMovingReportAsync(ReportOptions o)
    {
        var data = await SlowMovingDataAsync(o.Days);
        if (o.CategoryId.HasValue) data = data.Where(d => d.Product.CategoryId == o.CategoryId).ToList();
        if (o.Slow == SlowFilter.DeadOnly) data = data.Where(d => d.IsDead).ToList();
        if (o.Slow == SlowFilter.SlowOnly) data = data.Where(d => !d.IsDead).ToList();

        var r = new ReportResult
        {
            Title = "Slow-Moving & Dead Stock",
            Subtitle = $"No sales (dead) or {settings.Current.SlowMovingMaxQty} unit(s) or fewer sold (slow) in the last {o.Days} days",
            Landscape = true,
            Columns = [new("SKU", ColKind.Mono, 1.2f), new("Product", ColKind.Text, 2.6f), new("Category", ColKind.Text, 1.1f), new("Supplier", ColKind.Text, 1.4f),
                       new("On Hand", ColKind.Number, .7f), new($"Sold ({o.Days}d)", ColKind.Number, .7f), new("Last Sold", ColKind.Date, 1f),
                       new("Days Idle", ColKind.Number, .7f), new("Value at Cost", ColKind.Money, 1f), new("Status", ColKind.Status, .9f)],
            Note = "Tip: put dead stock on sale, bundle it with best-sellers (e.g. mouse + keyboard), or ask the supplier about a return/exchange. Avoid re-ordering these items."
        };
        foreach (var d in data)
            r.Rows.Add([d.Product.Sku, d.Product.Name, d.Product.Category?.Name, d.Product.Supplier?.Name ?? "—", d.Product.Stock, d.SoldQty,
                        d.LastSold, d.DaysSinceLastSale?.ToString() ?? "Never sold", d.ValueAtCost, d.IsDead ? "Dead Stock" : "Slow-Moving"]);
        r.Totals = [null, "TOTAL", null, null, data.Sum(d => d.Product.Stock), data.Sum(d => d.SoldQty), null, null, data.Sum(d => d.ValueAtCost), null];
        r.Summary =
        [
            ("Dead stock items", data.Count(d => d.IsDead).ToString()),
            ("Slow-moving items", data.Count(d => !d.IsDead).ToString()),
            ("Capital tied up (cost)", Helpers.Fmt.Peso(data.Sum(d => d.ValueAtCost))),
            ("Units sitting idle", data.Sum(d => d.Product.Stock).ToString("N0")),
        ];
        return r;
    }

    // ─────────────── Valuation ───────────────
    private async Task<ReportResult> ValuationAsync(ReportOptions o)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.Products.AsNoTracking().Include(p => p.Category).Where(p => !p.IsArchived);
        if (o.CategoryId.HasValue) q = q.Where(p => p.CategoryId == o.CategoryId);
        var ps = (await q.ToListAsync()).OrderBy(p => p.Category!.Name).ThenBy(p => p.Name).ToList();
        var r = new ReportResult
        {
            Title = "Inventory Valuation", Subtitle = $"As of {DateTime.Now:MMM d, yyyy h:mm tt}", Landscape = true,
            Columns = [new("SKU", ColKind.Mono, 1.2f), new("Product", ColKind.Text, 2.6f), new("Category", ColKind.Text, 1.2f), new("Stock", ColKind.Number, .6f),
                       new("Unit Cost", ColKind.Money), new("Value at Cost", ColKind.Money), new("Unit Price", ColKind.Money), new("Value at Price", ColKind.Money)]
        };
        foreach (var p in ps)
            r.Rows.Add([p.Sku, p.Name, p.Category?.Name, p.Stock, p.CostPrice, p.Stock * p.CostPrice, p.SellingPrice, p.Stock * p.SellingPrice]);
        decimal cost = ps.Sum(p => p.Stock * p.CostPrice), retail = ps.Sum(p => p.Stock * p.SellingPrice);
        r.Totals = [null, "TOTAL", null, ps.Sum(p => p.Stock), null, cost, null, retail];
        r.Summary = [("Products", ps.Count.ToString()), ("Units on hand", ps.Sum(p => p.Stock).ToString("N0")),
                     ("Value at cost", Helpers.Fmt.Peso(cost)), ("Value at price", Helpers.Fmt.Peso(retail)), ("Potential profit", Helpers.Fmt.Peso(retail - cost))];
        return r;
    }

    // ─────────────── Low stock (staff can print this) ───────────────
    private async Task<ReportResult> LowStockAsync(ReportOptions o)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Supplier)
            .Where(p => !p.IsArchived && p.Stock <= p.ReorderLevel);
        if (o.NoSupplierOnly) q = q.Where(p => p.SupplierId == null);
        else if (o.SupplierId.HasValue) q = q.Where(p => p.SupplierId == o.SupplierId);
        var ps = (await q.ToListAsync()).OrderBy(p => p.Supplier?.Name ?? "~").ThenBy(p => p.Stock).ToList();

        string subtitle = "All suppliers";
        if (o.NoSupplierOnly) subtitle = "Products without a supplier";
        else if (o.SupplierId.HasValue)
        {
            var s = await db.Suppliers.FindAsync(o.SupplierId.Value);
            if (s != null) subtitle = $"For: {s.Name}" + (s.ContactPerson != null ? $" — Attn: {s.ContactPerson}" : "") + (s.Phone != null ? $" · {s.Phone}" : "") + (s.Email != null ? $" · {s.Email}" : "");
        }

        // Suggested order: bring stock back up to twice the reorder level (at least 1).
        static int Suggest(Product p) => Math.Max(p.ReorderLevel * 2 - p.Stock, Math.Max(p.ReorderLevel, 1));

        var r = new ReportResult
        {
            Title = "Low Stock / Reorder List", Subtitle = subtitle, SignatureLines = true,
            Columns = [new("Supplier", ColKind.Text, 1.5f), new("SKU", ColKind.Mono, 1.2f), new("Product", ColKind.Text, 2.6f),
                       new("On Hand", ColKind.Number, .7f), new("Reorder Level", ColKind.Number, .8f), new("Suggested Qty", ColKind.Number, .9f), new("Status", ColKind.Status, .9f)],
            Note = "Suggested quantity brings stock back to twice the reorder level. Adjust as needed before sending to the supplier."
        };
        foreach (var p in ps)
            r.Rows.Add([p.Supplier?.Name ?? "— none —", p.Sku, p.Name, p.Stock, p.ReorderLevel, Suggest(p), p.Stock <= 0 ? "Out of Stock" : "Low Stock"]);
        r.Totals = [null, null, $"TOTAL ITEMS: {ps.Count}", null, null, ps.Sum(p => Suggest(p)), null];
        r.Summary = [("Items to reorder", ps.Count.ToString()), ("Out of stock", ps.Count(p => p.Stock <= 0).ToString()), ("Suggested units", ps.Sum(p => Suggest(p)).ToString("N0"))];
        return r;
    }
}
