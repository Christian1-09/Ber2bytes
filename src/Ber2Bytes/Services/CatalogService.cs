using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

public enum StockFilter { All, InStock, LowStock, OutOfStock }

public class ProductQuery
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public int? SupplierId { get; set; }
    public StockFilter Stock { get; set; } = StockFilter.All;
    public bool IncludeArchived { get; set; }
    public string Sort { get; set; } = "name";
}

/// <summary>Products, categories and suppliers.</summary>
public class CatalogService(IDbContextFactory<AppDbContext> dbf, AuthService auth, AppState state, ImageService images)
{
    // ───────────────────────── Products ─────────────────────────
    public async Task<List<Product>> GetProductsAsync(ProductQuery? query = null)
    {
        query ??= new ProductQuery();
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Supplier).AsQueryable();

        if (!query.IncludeArchived) q = q.Where(p => !p.IsArchived);
        if (query.CategoryId.HasValue) q = q.Where(p => p.CategoryId == query.CategoryId);
        if (query.SupplierId.HasValue) q = q.Where(p => p.SupplierId == query.SupplierId);
        q = query.Stock switch
        {
            StockFilter.InStock => q.Where(p => p.Stock > p.ReorderLevel),
            StockFilter.LowStock => q.Where(p => p.Stock > 0 && p.Stock <= p.ReorderLevel),
            StockFilter.OutOfStock => q.Where(p => p.Stock <= 0),
            _ => q
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(s) || p.Sku.ToLower().Contains(s)
                          || (p.Barcode != null && p.Barcode.Contains(s)) || (p.Brand != null && p.Brand.ToLower().Contains(s)));
        }
        q = query.Sort switch
        {
            "stock" => q.OrderBy(p => p.Stock).ThenBy(p => p.Name),
            "price" => q.OrderByDescending(p => p.SellingPrice),
            "newest" => q.OrderByDescending(p => p.Id),
            _ => q.OrderBy(p => p.Name)
        };
        return await q.ToListAsync();
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Supplier).FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>Exact barcode or SKU match (used by the scanner in POS).</summary>
    public async Task<Product?> FindByCodeAsync(string code)
    {
        code = code.Trim();
        var upper = code.ToUpper();
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Products.AsNoTracking().Include(p => p.Category)
            .FirstOrDefaultAsync(p => !p.IsArchived && (p.Barcode == code || p.Sku.ToUpper() == upper));
    }

    /// <summary>Validates and saves a product. Returns an error message, or null when saved.</summary>
    public async Task<string?> SaveProductAsync(Product input, byte[]? newImage, bool removeImage)
    {
        if (!auth.Can(Perm.ProductsManage)) return "Your role cannot add or edit products.";

        input.Name = input.Name.Trim();
        input.Sku = input.Sku.Trim().ToUpperInvariant();
        input.Barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim();
        input.Brand = input.Brand?.Trim();
        input.CostPrice = Math.Round(input.CostPrice, 2);
        input.SellingPrice = Math.Round(input.SellingPrice, 2);

        if (input.Name.Length == 0) return "Product name is required.";
        if (input.Sku.Length == 0) return "SKU is required.";
        if (input.CategoryId == 0) return "Please choose a category.";
        if (input.CostPrice < 0) return "Cost price cannot be negative.";
        if (input.SellingPrice <= 0) return "Selling price must be greater than 0.";
        if (input.ReorderLevel < 0) return "Reorder level cannot be negative.";
        if (input.Id == 0 && input.Stock < 0) return "Opening stock cannot be negative.";

        await using var db = await dbf.CreateDbContextAsync();
        if (await db.Products.AnyAsync(p => p.Id != input.Id && p.Sku == input.Sku))
            return $"SKU \"{input.Sku}\" is already used by another product.";
        if (input.Barcode != null && await db.Products.AnyAsync(p => p.Id != input.Id && p.Barcode == input.Barcode))
            return $"Barcode \"{input.Barcode}\" is already used by another product.";

        string? newFile = null;
        if (newImage != null)
        {
            try { newFile = images.SaveProductImage(newImage); }
            catch { return "That image could not be read. Please use a JPG or PNG file."; }
        }

        if (input.Id == 0)
        {
            var p = new Product
            {
                Name = input.Name, Sku = input.Sku, Barcode = input.Barcode, Brand = input.Brand,
                CategoryId = input.CategoryId, SupplierId = input.SupplierId, CostPrice = input.CostPrice,
                SellingPrice = input.SellingPrice, Stock = input.Stock, ReorderLevel = input.ReorderLevel,
                Description = input.Description, ImageFile = newFile, CreatedAt = DateTime.Now
            };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            if (p.Stock > 0)
                db.StockMovements.Add(new StockMovement
                {
                    Type = MovementType.StockIn, ProductId = p.Id, Quantity = p.Stock, StockAfter = p.Stock,
                    Reference = "OPENING", SupplierId = p.SupplierId, UnitCost = p.CostPrice, UserId = auth.UserId, Notes = "Opening stock"
                });
            AuditService.Add(db, auth.UserId, auth.UserName, "Add product", $"{p.Name} ({p.Sku})");
            await db.SaveChangesAsync();
        }
        else
        {
            var p = await db.Products.FindAsync(input.Id);
            if (p == null) return "This product no longer exists.";
            var changes = new List<string>();
            if (p.SellingPrice != input.SellingPrice) changes.Add($"price {p.SellingPrice:N2} → {input.SellingPrice:N2}");
            if (p.CostPrice != input.CostPrice) changes.Add($"cost {p.CostPrice:N2} → {input.CostPrice:N2}");

            var oldFile = p.ImageFile;
            p.Name = input.Name; p.Sku = input.Sku; p.Barcode = input.Barcode; p.Brand = input.Brand;
            p.CategoryId = input.CategoryId; p.SupplierId = input.SupplierId; p.CostPrice = input.CostPrice;
            p.SellingPrice = input.SellingPrice; p.ReorderLevel = input.ReorderLevel; p.Description = input.Description;
            p.UpdatedAt = DateTime.Now;
            if (newFile != null) p.ImageFile = newFile;
            else if (removeImage) p.ImageFile = null;

            AuditService.Add(db, auth.UserId, auth.UserName, "Edit product",
                $"{p.Name} ({p.Sku}){(changes.Count > 0 ? " — " + string.Join(", ", changes) : "")}");
            await db.SaveChangesAsync();
            if (oldFile != p.ImageFile) images.Delete(oldFile);
        }

        state.NotifyDataChanged();
        return null;
    }

    /// <summary>
    /// Products with sales or stock history are archived (hidden but kept for reports).
    /// Products with no history are deleted for good.
    /// </summary>
    public async Task<string> DeleteOrArchiveProductAsync(int id)
    {
        if (!auth.Can(Perm.ProductsManage)) return "Your role cannot delete products.";
        await using var db = await dbf.CreateDbContextAsync();
        var p = await db.Products.FindAsync(id);
        if (p == null) return "Product not found.";

        bool hasHistory = await db.SaleItems.AnyAsync(i => i.ProductId == id)
                          || await db.StockMovements.AnyAsync(m => m.ProductId == id && m.Reference != "OPENING")
                          || await db.Returns.AnyAsync(r => r.ProductId == id);
        string result;
        if (hasHistory)
        {
            p.IsArchived = true;
            p.UpdatedAt = DateTime.Now;
            AuditService.Add(db, auth.UserId, auth.UserName, "Archive product", p.Name);
            result = $"\"{p.Name}\" has sales history, so it was archived instead of deleted. You can restore it from \"Show archived\".";
        }
        else
        {
            await db.StockMovements.Where(m => m.ProductId == id).ExecuteDeleteAsync();
            db.Products.Remove(p);
            AuditService.Add(db, auth.UserId, auth.UserName, "Delete product", p.Name);
            result = $"\"{p.Name}\" was deleted.";
            images.Delete(p.ImageFile);
        }
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
        return result;
    }

    public async Task RestoreProductAsync(int id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var p = await db.Products.FindAsync(id);
        if (p == null) return;
        p.IsArchived = false;
        AuditService.Add(db, auth.UserId, auth.UserName, "Restore product", p.Name);
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
    }

    /// <summary>Quantity sold and last sale date per product within the last N days.</summary>
    public async Task<(int SoldQty, DateTime? LastSold)> GetSalesSummaryAsync(int productId, int days)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var since = DateTime.Today.AddDays(-days);
        var items = db.SaleItems.Where(i => i.ProductId == productId && i.Sale!.Status == SaleStatus.Completed);
        var qty = await items.Where(i => i.Sale!.Date >= since).SumAsync(i => (int?)(i.Quantity - i.ReturnedQty)) ?? 0;
        var last = await items.MaxAsync(i => (DateTime?)i.Sale!.Date);
        return (qty, last);
    }

    // ───────────────────────── Categories ─────────────────────────
    public async Task<List<(Category Category, int ProductCount, int LowCount)>> GetCategoriesWithCountsAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        var cats = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var counts = await db.Products.Where(p => !p.IsArchived)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count(), Low = g.Count(p => p.Stock <= p.ReorderLevel) })
            .ToListAsync();
        return cats.Select(c =>
        {
            var x = counts.FirstOrDefault(k => k.Key == c.Id);
            return (c, x?.Count ?? 0, x?.Low ?? 0);
        }).ToList();
    }

    public async Task<List<Category>> GetCategoriesAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<string?> SaveCategoryAsync(Category input)
    {
        if (!auth.Can(Perm.Categories)) return "Your role cannot manage categories.";
        input.Name = input.Name.Trim();
        if (input.Name.Length == 0) return "Category name is required.";
        await using var db = await dbf.CreateDbContextAsync();
        if (await db.Categories.AnyAsync(c => c.Id != input.Id && c.Name.ToLower() == input.Name.ToLower()))
            return "A category with this name already exists.";

        if (input.Id == 0)
            db.Categories.Add(new Category { Name = input.Name, Description = input.Description, Icon = input.Icon, Color = input.Color });
        else
        {
            var c = await db.Categories.FindAsync(input.Id);
            if (c == null) return "Category not found.";
            c.Name = input.Name; c.Description = input.Description; c.Icon = input.Icon; c.Color = input.Color;
        }
        AuditService.Add(db, auth.UserId, auth.UserName, input.Id == 0 ? "Add category" : "Edit category", input.Name);
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
        return null;
    }

    public async Task<string?> DeleteCategoryAsync(int id)
    {
        if (!auth.Can(Perm.Categories)) return "Your role cannot manage categories.";
        await using var db = await dbf.CreateDbContextAsync();
        var c = await db.Categories.FindAsync(id);
        if (c == null) return "Category not found.";
        int n = await db.Products.CountAsync(p => p.CategoryId == id);
        if (n > 0) return $"\"{c.Name}\" still has {n} product(s) (including archived). Move them to another category first.";
        db.Categories.Remove(c);
        AuditService.Add(db, auth.UserId, auth.UserName, "Delete category", c.Name);
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
        return null;
    }

    // ───────────────────────── Suppliers ─────────────────────────
    public record SupplierRow(Supplier Supplier, int ProductCount, int LowCount);

    public async Task<List<SupplierRow>> GetSuppliersWithCountsAsync(string? search = null)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.Suppliers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x => x.Name.ToLower().Contains(s) || (x.ContactPerson != null && x.ContactPerson.ToLower().Contains(s)));
        }
        var sups = await q.OrderBy(x => x.Name).ToListAsync();
        var counts = await db.Products.Where(p => !p.IsArchived && p.SupplierId != null)
            .GroupBy(p => p.SupplierId!.Value)
            .Select(g => new { g.Key, Count = g.Count(), Low = g.Count(p => p.Stock <= p.ReorderLevel) })
            .ToListAsync();
        return sups.Select(s =>
        {
            var x = counts.FirstOrDefault(k => k.Key == s.Id);
            return new SupplierRow(s, x?.Count ?? 0, x?.Low ?? 0);
        }).ToList();
    }

    public async Task<List<Supplier>> GetSuppliersAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
    }

    public async Task<string?> SaveSupplierAsync(Supplier input)
    {
        if (!auth.Can(Perm.SuppliersManage)) return "Your role cannot manage suppliers.";
        input.Name = input.Name.Trim();
        if (input.Name.Length == 0) return "Supplier name is required.";
        if (!string.IsNullOrWhiteSpace(input.Email) && !System.Text.RegularExpressions.Regex.IsMatch(input.Email.Trim(), @"^\S+@\S+\.\S+$"))
            return "Please enter a valid email address.";

        await using var db = await dbf.CreateDbContextAsync();
        if (await db.Suppliers.AnyAsync(s => s.Id != input.Id && s.Name.ToLower() == input.Name.ToLower()))
            return "A supplier with this name already exists.";
        if (input.Id == 0)
            db.Suppliers.Add(new Supplier
            {
                Name = input.Name, ContactPerson = input.ContactPerson?.Trim(), Phone = input.Phone?.Trim(),
                Email = input.Email?.Trim(), Address = input.Address?.Trim(), PaymentTerms = input.PaymentTerms
            });
        else
        {
            var s = await db.Suppliers.FindAsync(input.Id);
            if (s == null) return "Supplier not found.";
            s.Name = input.Name; s.ContactPerson = input.ContactPerson?.Trim(); s.Phone = input.Phone?.Trim();
            s.Email = input.Email?.Trim(); s.Address = input.Address?.Trim(); s.PaymentTerms = input.PaymentTerms;
        }
        AuditService.Add(db, auth.UserId, auth.UserName, input.Id == 0 ? "Add supplier" : "Edit supplier", input.Name);
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
        return null;
    }

    public async Task<string?> DeleteSupplierAsync(int id)
    {
        if (!auth.Can(Perm.SuppliersManage)) return "Your role cannot manage suppliers.";
        await using var db = await dbf.CreateDbContextAsync();
        var s = await db.Suppliers.FindAsync(id);
        if (s == null) return "Supplier not found.";
        // Products keep working; their supplier is cleared (SetNull).
        await db.Products.Where(p => p.SupplierId == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.SupplierId, (int?)null));
        db.Suppliers.Remove(s);
        AuditService.Add(db, auth.UserId, auth.UserName, "Delete supplier", s.Name);
        await db.SaveChangesAsync();
        state.NotifyDataChanged();
        return null;
    }
}
