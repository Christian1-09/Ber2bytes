using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

public class CartLine
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public int Available { get; set; }
    public string? ImageFile { get; set; }
    public string Icon { get; set; } = "box";
    public string Color { get; set; } = "#E11D2A";
    public decimal LineTotal => Price * Quantity;
}

public class HeldCart
{
    public List<CartLine> Lines { get; set; } = [];
    public string? Customer { get; set; }
    public string? Phone { get; set; }
    public DateTime HeldAt { get; set; } = DateTime.Now;
    public decimal Total => Lines.Sum(l => l.LineTotal);
}

/// <summary>
/// The POS cart lives here (not in the page) so it survives switching pages.
/// It is cleared when the user signs out.
/// </summary>
public class PosCart
{
    public List<CartLine> Lines { get; } = [];
    public List<HeldCart> Held { get; } = [];
    public string? Customer { get; set; }
    public string? Phone { get; set; }
    public decimal? DiscountValue { get; set; }
    public bool DiscountIsPercent { get; set; } = true;
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public decimal? Tendered { get; set; }
    public string? ReferenceNo { get; set; }

    public int ItemCount => Lines.Sum(l => l.Quantity);
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    public decimal Discount
    {
        get
        {
            var v = Math.Max(0, DiscountValue ?? 0);
            var d = DiscountIsPercent ? Subtotal * Math.Min(v, 100) / 100m : Math.Min(v, Subtotal);
            return Math.Round(d, 2);
        }
    }

    public decimal DiscountPercent => Subtotal == 0 ? 0 : Discount / Subtotal * 100m;
    public decimal Total => Math.Round(Subtotal - Discount, 2);
    public decimal Change => Method == PaymentMethod.Cash ? Math.Max(0, (Tendered ?? 0) - Total) : 0;
    public bool IsShort => Method == PaymentMethod.Cash && (Tendered ?? 0) > 0 && (Tendered ?? 0) < Total;

    public void ResetPayment()
    {
        Customer = null; Phone = null; DiscountValue = null; DiscountIsPercent = true;
        Method = PaymentMethod.Cash; Tendered = null; ReferenceNo = null;
    }

    public void Clear()
    {
        Lines.Clear();
        ResetPayment();
    }

    public void ClearAll()
    {
        Clear();
        Held.Clear();
    }
}

public class SalesQuery
{
    public string? Search { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public PaymentMethod? Method { get; set; }
    public SaleStatus? Status { get; set; }
}

public class SalesService(IDbContextFactory<AppDbContext> dbf, AuthService auth, SettingsService settings, AppState state)
{
    /// <summary>Saves the sale, deducts stock and logs movements in one database transaction.</summary>
    public async Task<(Sale? Sale, string? Error)> CheckoutAsync(PosCart cart)
    {
        if (!auth.Can(Perm.Pos)) return (null, "Your role cannot make sales.");
        if (cart.Lines.Count == 0) return (null, "The cart is empty.");
        if (!auth.IsAdmin && cart.DiscountPercent > settings.Current.StaffMaxDiscountPercent + 0.001m)
            return (null, $"Staff can give up to {settings.Current.StaffMaxDiscountPercent:0.##}% discount only. Ask an administrator for a bigger discount.");
        if (cart.Method == PaymentMethod.Cash && (cart.Tendered ?? 0) < cart.Total)
            return (null, "Amount tendered is less than the total.");
        if (cart.Method == PaymentMethod.GCash && string.IsNullOrWhiteSpace(cart.ReferenceNo))
            return (null, "Please enter the GCash reference number.");

        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var ids = cart.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        foreach (var line in cart.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var p) || p.IsArchived)
                return (null, $"\"{line.Name}\" is no longer available.");
            if (line.Quantity <= 0) return (null, $"Invalid quantity for \"{line.Name}\".");
            if (p.Stock < line.Quantity)
                return (null, $"Only {p.Stock} × \"{p.Name}\" left in stock. Please update the cart.");
        }

        var lastNo = await db.Sales.OrderByDescending(s => s.Id).Select(s => s.InvoiceNo).FirstOrDefaultAsync();
        int next = 1;
        if (lastNo != null && int.TryParse(lastNo.Replace("OR-", ""), out var n)) next = n + 1;

        var sale = new Sale
        {
            InvoiceNo = $"OR-{next:000000}",
            Date = DateTime.Now,
            CustomerName = string.IsNullOrWhiteSpace(cart.Customer) ? "Walk-in" : cart.Customer.Trim(),
            CustomerPhone = cart.Phone?.Trim(),
            Subtotal = cart.Subtotal,
            Discount = cart.Discount,
            Total = cart.Total,
            PaymentMethod = cart.Method,
            ReferenceNo = cart.Method == PaymentMethod.GCash ? cart.ReferenceNo?.Trim() : null,
            AmountTendered = cart.Method == PaymentMethod.Cash ? cart.Tendered ?? 0 : cart.Total,
            Change = cart.Change,
            CashierId = auth.UserId,
        };

        foreach (var line in cart.Lines)
        {
            var p = products[line.ProductId];
            p.Stock -= line.Quantity;
            sale.Items.Add(new SaleItem
            {
                ProductId = p.Id, ProductName = p.Name, Sku = p.Sku, Quantity = line.Quantity,
                UnitPrice = p.SellingPrice, UnitCost = p.CostPrice
            });
            db.StockMovements.Add(new StockMovement
            {
                Type = MovementType.Sale, ProductId = p.Id, Quantity = -line.Quantity, StockAfter = p.Stock,
                Reference = sale.InvoiceNo, UserId = auth.UserId, Notes = sale.CustomerName
            });
        }
        // Prices are re-read from the database so the sale always uses the current price.
        sale.Subtotal = sale.Items.Sum(i => i.LineTotal);
        sale.Total = Math.Round(sale.Subtotal - Math.Min(sale.Discount, sale.Subtotal), 2);
        if (sale.PaymentMethod == PaymentMethod.Cash)
        {
            if (sale.AmountTendered < sale.Total) return (null, "Prices changed. Please check the total and tendered amount.");
            sale.Change = sale.AmountTendered - sale.Total;
        }
        else sale.AmountTendered = sale.Total;

        db.Sales.Add(sale);
        AuditService.Add(db, auth.UserId, auth.UserName, "Sale", $"{sale.InvoiceNo} — ₱{sale.Total:N2} ({sale.Items.Count} item line/s, {sale.PaymentMethod})");
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        state.NotifyDataChanged();
        sale.Cashier = auth.CurrentUser;
        return (sale, null);
    }

    public async Task<List<Sale>> SearchAsync(SalesQuery q)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var s = db.Sales.AsNoTracking().Include(x => x.Items).Include(x => x.Cashier).AsQueryable();
        if (!auth.Can(Perm.SalesViewAll)) s = s.Where(x => x.CashierId == auth.UserId);
        if (q.From.HasValue) s = s.Where(x => x.Date >= q.From.Value.Date);
        if (q.To.HasValue) s = s.Where(x => x.Date < q.To.Value.Date.AddDays(1));
        if (q.Method.HasValue) s = s.Where(x => x.PaymentMethod == q.Method);
        if (q.Status.HasValue) s = s.Where(x => x.Status == q.Status);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var t = q.Search.Trim().ToLower();
            s = s.Where(x => x.InvoiceNo.ToLower().Contains(t) || x.CustomerName.ToLower().Contains(t)
                          || (x.ReferenceNo != null && x.ReferenceNo.Contains(t)));
        }
        return await s.OrderByDescending(x => x.Date).Take(2000).ToListAsync();
    }

    public async Task<Sale?> GetAsync(int id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Sales.AsNoTracking().Include(x => x.Items).Include(x => x.Cashier).FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<Sale?> GetByInvoiceAsync(string invoiceNo)
    {
        var no = invoiceNo.Trim().ToUpper();
        if (int.TryParse(no, out var num)) no = $"OR-{num:000000}"; // allow typing just "25"
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Sales.AsNoTracking().Include(x => x.Items).Include(x => x.Cashier).FirstOrDefaultAsync(x => x.InvoiceNo == no);
    }

    /// <summary>Cancels a sale and puts the items back in stock. Admin only.</summary>
    public async Task<string?> VoidAsync(int saleId, string reason)
    {
        if (!auth.Can(Perm.SalesVoid)) return "Only an administrator can void sales.";
        if (string.IsNullOrWhiteSpace(reason)) return "Please give a reason.";

        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleId);
        if (sale == null) return "Sale not found.";
        if (sale.Status == SaleStatus.Voided) return "This sale is already voided.";
        if (await db.Returns.AnyAsync(r => r.SaleId == saleId && r.Status != ReturnStatus.Rejected))
            return "This sale has a return on file. Handle it in Returns instead of voiding.";

        foreach (var item in sale.Items)
        {
            var p = await db.Products.FindAsync(item.ProductId);
            if (p == null) continue;
            p.Stock += item.Quantity;
            db.StockMovements.Add(new StockMovement
            {
                Type = MovementType.Void, ProductId = p.Id, Quantity = item.Quantity, StockAfter = p.Stock,
                Reference = sale.InvoiceNo, UserId = auth.UserId, Notes = "Voided: " + reason.Trim()
            });
        }
        sale.Status = SaleStatus.Voided;
        sale.VoidReason = reason.Trim();
        sale.VoidedAt = DateTime.Now;
        sale.VoidedById = auth.UserId;
        AuditService.Add(db, auth.UserId, auth.UserName, "Void sale", $"{sale.InvoiceNo} — ₱{sale.Total:N2} — {reason.Trim()}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        state.NotifyDataChanged();
        return null;
    }
}
