using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

public record ChartPoint(string Label, decimal Value, string? Tooltip = null);

public class DashboardData
{
    public int TotalProducts { get; set; }
    public int ProductsAddedThisMonth { get; set; }
    public int TotalUnits { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal TodaySales { get; set; }
    public decimal YesterdaySales { get; set; }
    public int TodayTransactions { get; set; }
    public int TodayItems { get; set; }
    public decimal TodayProfit { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int PendingReturns { get; set; }

    public List<ChartPoint> Daily { get; set; } = [];
    public List<ChartPoint> Weekly { get; set; } = [];
    public List<ChartPoint> Monthly { get; set; } = [];
    public List<ChartPoint> ByCategory { get; set; } = [];

    public List<Sale> RecentSales { get; set; } = [];
    public List<Product> LowStock { get; set; } = [];
    public List<(int ProductId, string Name, string Sku, int Qty, decimal Revenue, decimal Profit)> BestSellers { get; set; } = [];
    public List<SlowItem> SlowMoving { get; set; } = [];

    public decimal? ChangePercent => YesterdaySales == 0 ? null : Math.Round((TodaySales - YesterdaySales) / YesterdaySales * 100m, 0);
}

public class DashboardService(IDbContextFactory<AppDbContext> dbf, AuthService auth, ReportService reports, SettingsService settings)
{
    public async Task<DashboardData> LoadAsync()
    {
        var d = new DashboardData();
        bool all = auth.Can(Perm.SalesViewAll);
        int me = auth.UserId;
        var today = DateTime.Today;

        await using var db = await dbf.CreateDbContextAsync();

        var products = await db.Products.AsNoTracking().Include(p => p.Category).Where(p => !p.IsArchived).ToListAsync();
        d.TotalProducts = products.Count;
        d.ProductsAddedThisMonth = products.Count(p => p.CreatedAt >= new DateTime(today.Year, today.Month, 1));
        d.TotalUnits = products.Sum(p => p.Stock);
        d.InventoryValue = products.Sum(p => p.Stock * p.CostPrice);
        d.LowStockCount = products.Count(p => p.Stock <= p.ReorderLevel);
        d.OutOfStockCount = products.Count(p => p.Stock <= 0);
        d.LowStock = products.Where(p => p.Stock <= p.ReorderLevel).OrderBy(p => p.Stock).ThenBy(p => p.Name).Take(6).ToList();
        d.PendingReturns = await db.Returns.CountAsync(r => r.Status == ReturnStatus.Pending);

        // Sales in the last ~6 months (enough for all charts), loaded once and grouped in memory.
        var since = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
        var salesQ = db.Sales.AsNoTracking().Include(s => s.Items).Include(s => s.Cashier)
            .Where(s => s.Status == SaleStatus.Completed && s.Date >= since);
        if (!all) salesQ = salesQ.Where(s => s.CashierId == me);
        var sales = await salesQ.ToListAsync();

        var todays = sales.Where(s => s.Date.Date == today).ToList();
        d.TodaySales = todays.Sum(s => s.Total);
        d.YesterdaySales = sales.Where(s => s.Date.Date == today.AddDays(-1)).Sum(s => s.Total);
        d.TodayTransactions = todays.Count;
        d.TodayItems = todays.Sum(s => s.Items.Sum(i => i.Quantity));
        d.TodayProfit = todays.Sum(s => s.Items.Sum(i => (i.UnitPrice - i.UnitCost) * i.Quantity) - s.Discount);

        // Daily: last 7 days
        for (int i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var v = sales.Where(s => s.Date.Date == day).Sum(s => s.Total);
            d.Daily.Add(new ChartPoint(i == 0 ? "Today" : day.ToString("ddd"), v, $"{day:MMM d}: ₱{v:N2}"));
        }
        // Weekly: last 8 weeks (weeks start Monday)
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        for (int i = 7; i >= 0; i--)
        {
            var ws = weekStart.AddDays(-7 * i);
            var v = sales.Where(s => s.Date >= ws && s.Date < ws.AddDays(7)).Sum(s => s.Total);
            d.Weekly.Add(new ChartPoint(i == 0 ? "This wk" : ws.ToString("MMM d"), v, $"Week of {ws:MMM d}: ₱{v:N2}"));
        }
        // Monthly: last 6 months
        for (int i = 5; i >= 0; i--)
        {
            var ms = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
            var v = sales.Where(s => s.Date >= ms && s.Date < ms.AddMonths(1)).Sum(s => s.Total);
            d.Monthly.Add(new ChartPoint(ms.ToString("MMM"), v, $"{ms:MMMM yyyy}: ₱{v:N2}"));
        }
        // Sales by category: this month
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var catNames = products.ToDictionary(p => p.Id, p => p.Category?.Name ?? "Other");
        d.ByCategory = sales.Where(s => s.Date >= monthStart)
            .SelectMany(s => s.Items)
            .GroupBy(i => catNames.GetValueOrDefault(i.ProductId, "Other"))
            .Select(g => new ChartPoint(g.Key, g.Sum(i => i.UnitPrice * i.Quantity)))
            .OrderByDescending(c => c.Value).Take(6).ToList();

        d.RecentSales = sales.OrderByDescending(s => s.Date).Take(6).ToList();
        d.BestSellers = await reports.TopProductsDataAsync(today.AddDays(-29), today.AddDays(1), 5);
        d.SlowMoving = (await reports.SlowMovingDataAsync(settings.Current.DeadStockDays)).Take(5).ToList();
        return d;
    }
}

public enum AlertLevel { Danger, Warning, Info }
public record AlertItem(AlertLevel Level, string Icon, string Title, string Description, string? ActionText, string? ActionUrl, int? ProductId = null);

/// <summary>Builds the list on the Alerts page and the number on the bell.</summary>
public class AlertService(IDbContextFactory<AppDbContext> dbf, AuthService auth, ReportService reports, SettingsService settings)
{
    public async Task<List<AlertItem>> GetAsync()
    {
        var list = new List<AlertItem>();
        await using var db = await dbf.CreateDbContextAsync();
        var low = await db.Products.AsNoTracking().Include(p => p.Supplier)
            .Where(p => !p.IsArchived && p.Stock <= p.ReorderLevel).OrderBy(p => p.Stock).ToListAsync();

        foreach (var p in low.Where(p => p.Stock <= 0))
            list.Add(new(AlertLevel.Danger, "alert", $"Out of stock: {p.Name}",
                $"SKU {p.Sku} · Supplier: {p.Supplier?.Name ?? "—"} · Customers can't buy this item right now",
                auth.Can(Perm.StockIn) ? "Stock In" : null, null, p.Id));
        foreach (var p in low.Where(p => p.Stock > 0))
            list.Add(new(AlertLevel.Warning, "alert", $"Low stock: {p.Name}",
                $"{p.Stock} left (reorder level {p.ReorderLevel}) · Supplier: {p.Supplier?.Name ?? "—"}",
                auth.Can(Perm.StockIn) ? "Stock In" : null, null, p.Id));

        var pending = await db.Returns.AsNoTracking().Include(r => r.SaleItem).Include(r => r.Sale)
            .Where(r => r.Status == ReturnStatus.Pending).OrderBy(r => r.Id).ToListAsync();
        foreach (var r in pending)
            list.Add(new(AlertLevel.Info, "undo", $"Return {r.ReturnNo} is waiting for approval",
                $"{r.SaleItem?.ProductName} ×{r.Quantity} · {r.Sale?.CustomerName} — {r.Reason}",
                auth.Can(Perm.ReturnsProcess) ? "Review" : "View", "/returns"));

        if (auth.Can(Perm.ReportBusiness))
        {
            var days = settings.Current.DeadStockDays;
            var dead = (await reports.SlowMovingDataAsync(days)).Where(x => x.IsDead).ToList();
            if (dead.Count > 0)
                list.Add(new(AlertLevel.Info, "hourglass", $"{dead.Count} product(s) have not sold in {days}+ days",
                    $"₱{dead.Sum(x => x.ValueAtCost):N2} of capital is tied up in dead stock. Consider a sale or bundle.",
                    "Open report", "/reports?type=SlowMoving"));
        }
        return list;
    }

    /// <summary>Number shown on the bell (stock and pending-return alerts only).</summary>
    public async Task<(int Alerts, int LowStock, int PendingReturns)> CountsAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        int low = await db.Products.CountAsync(p => !p.IsArchived && p.Stock <= p.ReorderLevel);
        int pending = await db.Returns.CountAsync(r => r.Status == ReturnStatus.Pending);
        return (low + pending, low, pending);
    }
}
