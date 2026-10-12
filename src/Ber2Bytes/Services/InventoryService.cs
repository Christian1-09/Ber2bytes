using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

public class StockInForm
{
    public int ProductId { get; set; }
    public int? SupplierId { get; set; }
    public string? Reference { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal? UnitCost { get; set; }
    public string? Notes { get; set; }
}

public enum AdjustMode { Remove, Add, SetCount }

public class AdjustForm
{
    public int ProductId { get; set; }
    public AdjustMode Mode { get; set; } = AdjustMode.Remove;
    public int Quantity { get; set; }
    public string Reason { get; set; } = "Physical count correction";
    public string? Notes { get; set; }
}

public class InventoryService(IDbContextFactory<AppDbContext> dbf, AuthService auth, AppState state)
{
    public static readonly string[] AdjustReasons =
        ["Physical count correction", "Damaged", "Lost / Stolen", "Used as demo / display unit", "Other"];

    public async Task<List<StockMovement>> GetMovementsAsync(string? search, MovementType? type, DateTime? from, DateTime? to, int take = 500)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.StockMovements.AsNoTracking().Include(m => m.Product).Include(m => m.User).AsQueryable();
        if (type.HasValue) q = q.Where(m => m.Type == type);
        if (from.HasValue) q = q.Where(m => m.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(m => m.Date < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(m => m.Product!.Name.ToLower().Contains(s) || m.Product.Sku.ToLower().Contains(s)
                          || (m.Reference != null && m.Reference.ToLower().Contains(s)));
        }
        return await q.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).Take(take).ToListAsync();
    }

    public async Task<List<StockMovement>> GetProductMovementsAsync(int productId, int take = 10)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.StockMovements.AsNoTracking().Include(m => m.User)
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).Take(take).ToListAsync();
    }

    /// <summary>Receive a delivery. Returns an error message or null.</summary>
    public async Task<string?> StockInAsync(StockInForm f)
    {
        if (!auth.Can(Perm.StockIn)) return "Your role cannot receive stock.";
        if (f.ProductId == 0) return "Please choose a product.";
        if (f.Quantity <= 0) return "Quantity must be at least 1.";
        if (f.Quantity > 100_000) return "Quantity is too large.";
        if (f.UnitCost is < 0) return "Unit cost cannot be negative.";

        await using var db = await dbf.CreateDbContextAsync();
        var p = await db.Products.FindAsync(f.ProductId);
        if (p == null) return "Product not found.";

        p.Stock += f.Quantity;
        // Only admins may change the cost price.
        if (f.UnitCost is > 0 && auth.Can(Perm.ViewCost)) p.CostPrice = Math.Round(f.UnitCost.Value, 2);
        p.UpdatedAt = DateTime.Now;

        var reference = string.IsNullOrWhiteSpace(f.Reference) ? $"SI-{DateTime.Now:yyMMdd-HHmmss}" : f.Reference.Trim();
        db.StockMovements.Add(new StockMovement
        {
            Type = MovementType.StockIn, ProductId = p.Id, Quantity = f.Quantity, StockAfter = p.Stock,
            Reference = reference, SupplierId = f.SupplierId, UnitCost = p.CostPrice, UserId = auth.UserId,
            Notes = f.Notes?.Trim()
        });
        if (f.SupplierId.HasValue)
        {
            var s = await db.Suppliers.FindAsync(f.SupplierId.Value);
            if (s != null) s.LastDeliveryAt = DateTime.Now;
        }
        AuditService.Add(db, auth.UserId, auth.UserName, "Stock in", $"{p.Name} +{f.Quantity} ({reference})");
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
        return null;
    }

    /// <summary>Correct stock for damage, loss or a physical count. Admin only.</summary>
    public async Task<string?> AdjustAsync(AdjustForm f)
    {
        if (!auth.Can(Perm.StockAdjust)) return "Only an administrator can adjust stock.";
        if (f.ProductId == 0) return "Please choose a product.";
        if (f.Quantity < 0) return "Quantity cannot be negative.";
        if (string.IsNullOrWhiteSpace(f.Reason)) return "Please choose a reason.";

        await using var db = await dbf.CreateDbContextAsync();
        var p = await db.Products.FindAsync(f.ProductId);
        if (p == null) return "Product not found.";

        int change = f.Mode switch
        {
            AdjustMode.Add => f.Quantity,
            AdjustMode.Remove => -f.Quantity,
            _ => f.Quantity - p.Stock
        };
        if (change == 0) return "This does not change the stock.";
        if (p.Stock + change < 0) return $"Stock cannot go below 0 (current stock: {p.Stock}).";

        p.Stock += change;
        p.UpdatedAt = DateTime.Now;
        int count = await db.StockMovements.CountAsync(m => m.Type == MovementType.Adjustment) + 1;
        var note = f.Reason + (string.IsNullOrWhiteSpace(f.Notes) ? "" : " — " + f.Notes.Trim());
        db.StockMovements.Add(new StockMovement
        {
            Type = MovementType.Adjustment, ProductId = p.Id, Quantity = change, StockAfter = p.Stock,
            Reference = $"ADJ-{count:0000}", UserId = auth.UserId, Notes = note
        });
        AuditService.Add(db, auth.UserId, auth.UserName, "Stock adjustment", $"{p.Name} {(change > 0 ? "+" : "")}{change} ({f.Reason})");
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
        return null;
    }
}
