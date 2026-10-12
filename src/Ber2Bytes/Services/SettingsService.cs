using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

/// <summary>Store settings, cached in memory after the first read.</summary>
public class SettingsService(IDbContextFactory<AppDbContext> dbf, AuthService auth, AppState state)
{
    private StoreSettings? _cache;

    public StoreSettings Current
    {
        get
        {
            if (_cache == null)
            {
                using var db = dbf.CreateDbContext();
                _cache = db.Settings.AsNoTracking().First();
            }
            return _cache;
        }
    }

    public async Task<string?> SaveAsync(StoreSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.StoreName)) return "Store name is required.";
        if (s.StaffMaxDiscountPercent is < 0 or > 100) return "Staff discount limit must be between 0 and 100%.";
        if (s.DeadStockDays < 7) return "Dead stock days must be at least 7.";
        if (s.SlowMovingMaxQty < 0) return "Slow-moving quantity cannot be negative.";
        if (s.DefaultReorderLevel < 0) return "Reorder level cannot be negative.";
        if (s.ReturnWindowDays < 0) return "Return window cannot be negative.";

        await using var db = await dbf.CreateDbContextAsync();
        var row = await db.Settings.FirstAsync();
        db.Entry(row).CurrentValues.SetValues(s);
        row.Id = 1;
        AuditService.Add(db, auth.UserId, auth.UserName, "Settings", "Store settings updated");
        await db.SaveChangesAsync();
        _cache = null;
        state.NotifyDataChanged();
        return null;
    }

    public void ClearCache() => _cache = null;

    /// <summary>A copy the Settings page can edit without changing the cache until saved.</summary>
    public StoreSettings EditableCopy()
    {
        var c = Current;
        return new StoreSettings
        {
            Id = 1, StoreName = c.StoreName, BranchName = c.BranchName, Address = c.Address, Phone = c.Phone,
            ReceiptFooter = c.ReceiptFooter, DefaultReorderLevel = c.DefaultReorderLevel,
            StaffMaxDiscountPercent = c.StaffMaxDiscountPercent, DeadStockDays = c.DeadStockDays,
            SlowMovingMaxQty = c.SlowMovingMaxQty, ReturnWindowDays = c.ReturnWindowDays, AutoBackupOnExit = c.AutoBackupOnExit
        };
    }
}
