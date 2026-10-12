using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

/// <summary>Records who did what (shown in Activity Log).</summary>
public class AuditService(IDbContextFactory<AppDbContext> dbf)
{
    public async Task LogAsync(int? userId, string userName, string action, string details)
    {
        await using var db = await dbf.CreateDbContextAsync();
        db.AuditLogs.Add(new AuditLog { UserId = userId, UserName = userName, Action = action, Details = Trim(details, 500) });
        await db.SaveChangesAsync();
    }

    /// <summary>Adds the log entry to an existing context so it is saved in the same transaction.</summary>
    public static void Add(AppDbContext db, int? userId, string userName, string action, string details) =>
        db.AuditLogs.Add(new AuditLog { UserId = userId, UserName = userName, Action = action, Details = Trim(details, 500) });

    public async Task<List<AuditLog>> SearchAsync(string? search, DateTime? from, DateTime? to, int take = 500)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (from.HasValue) q = q.Where(a => a.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(a => a.Date < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(a => a.UserName.ToLower().Contains(s) || a.Action.ToLower().Contains(s) || a.Details.ToLower().Contains(s));
        }
        return await q.OrderByDescending(a => a.Id).Take(take).ToListAsync();
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max];
}
