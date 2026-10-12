using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

public class ReturnForm
{
    public int SaleId { get; set; }
    public int SaleItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public string Reason { get; set; } = "";
    public ItemCondition Condition { get; set; } = ItemCondition.Defective;
    public ReturnResolution Resolution { get; set; } = ReturnResolution.Replacement;
}

/// <summary>
/// Simple customer returns:
///  • Staff file a return → Pending. An admin then approves (Completed) or rejects it.
///  • When an admin files a return it is processed right away.
///  • Completed: a Resellable item goes back to stock; a Replacement takes one unit from stock;
///    a Refund records the amount to give back to the customer.
/// </summary>
public class ReturnService(IDbContextFactory<AppDbContext> dbf, AuthService auth, SettingsService settings, AppState state)
{
    public async Task<List<ReturnRequest>> SearchAsync(string? search, ReturnStatus? status)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.Returns.AsNoTracking().Include(r => r.Sale).Include(r => r.SaleItem)
            .Include(r => r.CreatedBy).Include(r => r.ProcessedBy).AsQueryable();
        if (status.HasValue) q = q.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(r => r.ReturnNo.ToLower().Contains(s) || r.Sale!.InvoiceNo.ToLower().Contains(s)
                          || r.SaleItem!.ProductName.ToLower().Contains(s) || r.Sale.CustomerName.ToLower().Contains(s));
        }
        return await q.OrderByDescending(r => r.Id).Take(1000).ToListAsync();
    }

    public async Task<Dictionary<ReturnStatus, int>> CountsAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        var rows = await db.Returns.GroupBy(r => r.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        return Enum.GetValues<ReturnStatus>().ToDictionary(s => s, s => rows.FirstOrDefault(r => r.Key == s)?.C ?? 0);
    }

    /// <summary>How many units of a sale line can still be returned (excluding pending requests).</summary>
    public async Task<Dictionary<int, int>> ReturnableQtyAsync(Sale sale)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var pending = await db.Returns.Where(r => r.SaleId == sale.Id && r.Status == ReturnStatus.Pending)
            .GroupBy(r => r.SaleItemId).Select(g => new { g.Key, Q = g.Sum(r => r.Quantity) }).ToListAsync();
        return sale.Items.ToDictionary(i => i.Id,
            i => i.Quantity - i.ReturnedQty - (pending.FirstOrDefault(p => p.Key == i.Id)?.Q ?? 0));
    }

    public bool IsWithinReturnWindow(Sale sale) =>
        (DateTime.Today - sale.Date.Date).TotalDays <= settings.Current.ReturnWindowDays;

    public async Task<(ReturnRequest? Request, string? Error)> CreateAsync(ReturnForm f)
    {
        if (!auth.Can(Perm.Returns)) return (null, "Your role cannot create returns.");
        if (string.IsNullOrWhiteSpace(f.Reason)) return (null, "Please describe the reason or problem.");
        if (f.Quantity <= 0) return (null, "Quantity must be at least 1.");

        await using var db = await dbf.CreateDbContextAsync();
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == f.SaleId);
        if (sale == null) return (null, "Receipt not found.");
        if (sale.Status == SaleStatus.Voided) return (null, "This sale was voided.");
        var item = sale.Items.FirstOrDefault(i => i.Id == f.SaleItemId);
        if (item == null) return (null, "Please choose the item being returned.");

        var pendingQty = await db.Returns.Where(r => r.SaleItemId == item.Id && r.Status == ReturnStatus.Pending).SumAsync(r => (int?)r.Quantity) ?? 0;
        var left = item.Quantity - item.ReturnedQty - pendingQty;
        if (f.Quantity > left) return (null, left <= 0 ? "All units of this item were already returned." : $"Only {left} unit(s) of this item can still be returned.");

        int next = (await db.Returns.MaxAsync(r => (int?)r.Id) ?? 0) + 1;
        var req = new ReturnRequest
        {
            ReturnNo = $"RT-{next:00000}", SaleId = sale.Id, SaleItemId = item.Id, ProductId = item.ProductId,
            Quantity = f.Quantity, Reason = f.Reason.Trim(), Condition = f.Condition, Resolution = f.Resolution,
            RefundAmount = f.Resolution == ReturnResolution.Refund ? RefundFor(sale, item, f.Quantity) : 0,
            CreatedById = auth.UserId, Status = ReturnStatus.Pending
        };
        db.Returns.Add(req);
        AuditService.Add(db, auth.UserId, auth.UserName, "Return filed", $"{req.ReturnNo} — {item.ProductName} ×{f.Quantity} ({sale.InvoiceNo})");
        await db.SaveChangesAsync();

        // Admins don't need to approve their own returns.
        if (auth.Can(Perm.ReturnsProcess))
        {
            var err = await ProcessAsync(req.Id, approve: true, note: "Processed at the counter by admin");
            if (err != null) return (req, "Return saved as Pending: " + err);
        }
        state.NotifyDataChanged();
        return (req, null);
    }

    /// <summary>Approve (and complete) or reject a pending return. Admin only.</summary>
    public async Task<string?> ProcessAsync(int returnId, bool approve, string? note)
    {
        if (!auth.Can(Perm.ReturnsProcess)) return "Only an administrator can approve or reject returns.";

        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = await db.Returns.Include(x => x.SaleItem).Include(x => x.Sale).FirstOrDefaultAsync(x => x.Id == returnId);
        if (r == null) return "Return not found.";
        if (r.Status != ReturnStatus.Pending) return "This return was already processed.";
        var item = r.SaleItem!;
        var p = await db.Products.FindAsync(r.ProductId);

        if (approve)
        {
            if (r.Resolution == ReturnResolution.Replacement)
            {
                if (p == null || p.Stock < r.Quantity)
                    return $"Not enough stock for a replacement (in stock: {p?.Stock ?? 0}). Receive stock first or choose Refund.";
            }
            if (p != null && r.Condition == ItemCondition.Resellable)
            {
                p.Stock += r.Quantity;
                db.StockMovements.Add(new StockMovement
                {
                    Type = MovementType.Return, ProductId = p.Id, Quantity = r.Quantity, StockAfter = p.Stock,
                    Reference = r.ReturnNo, UserId = auth.UserId, Notes = "Customer return (resellable)"
                });
            }
            if (p != null && r.Resolution == ReturnResolution.Replacement)
            {
                p.Stock -= r.Quantity;
                db.StockMovements.Add(new StockMovement
                {
                    Type = MovementType.Replacement, ProductId = p.Id, Quantity = -r.Quantity, StockAfter = p.Stock,
                    Reference = r.ReturnNo, UserId = auth.UserId, Notes = "Replacement unit given to " + r.Sale!.CustomerName
                });
            }
            if (r.Resolution == ReturnResolution.Refund)
            {
                item.ReturnedQty += r.Quantity; // refunded units no longer count as sold
                r.RefundAmount = RefundFor(r.Sale!, item, r.Quantity);
            }
            r.Status = ReturnStatus.Completed;
        }
        else
        {
            r.Status = ReturnStatus.Rejected;
        }
        r.ProcessedById = auth.UserId;
        r.ProcessedAt = DateTime.Now;
        r.AdminNote = note?.Trim();
        AuditService.Add(db, auth.UserId, auth.UserName, approve ? "Return completed" : "Return rejected",
            $"{r.ReturnNo} — {item.ProductName} ×{r.Quantity}{(r.RefundAmount > 0 && approve ? $" — refund ₱{r.RefundAmount:N2}" : "")}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        state.NotifyDataChanged();
        return null;
    }

    /// <summary>Refund = unit price minus this line's share of the sale discount.</summary>
    private static decimal RefundFor(Sale sale, SaleItem item, int qty)
    {
        var gross = item.UnitPrice * qty;
        var share = sale.Subtotal == 0 ? 0 : sale.Discount * gross / sale.Subtotal;
        return Math.Round(gross - share, 2);
    }
}
