using System.Text.RegularExpressions;
using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

public class UserForm
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Staff;
    public bool IsActive { get; set; } = true;
    public string? Password { get; set; }
}

public class UserService(IDbContextFactory<AppDbContext> dbf, AuthService auth)
{
    public async Task<List<User>> GetAllAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync();
    }

    public async Task<string?> SaveAsync(UserForm f)
    {
        if (!auth.Can(Perm.Users)) return "Only an administrator can manage users.";
        f.FullName = f.FullName.Trim();
        f.Username = f.Username.Trim().ToLowerInvariant();
        if (f.FullName.Length == 0) return "Full name is required.";
        if (!Regex.IsMatch(f.Username, "^[a-z0-9._]{3,30}$")) return "Username must be 3–30 characters: letters, numbers, dot or underscore.";
        bool isNew = f.Id == 0;
        if ((isNew || !string.IsNullOrEmpty(f.Password)) && (f.Password ?? "").Length < 6) return "Password must be at least 6 characters.";

        await using var db = await dbf.CreateDbContextAsync();
        if (await db.Users.AnyAsync(u => u.Id != f.Id && u.Username == f.Username)) return "That username is already taken.";

        User user;
        if (isNew)
        {
            user = new User { FullName = f.FullName, Username = f.Username, Role = f.Role, IsActive = f.IsActive, PasswordHash = BCrypt.Net.BCrypt.HashPassword(f.Password) };
            db.Users.Add(user);
        }
        else
        {
            user = await db.Users.FindAsync(f.Id) ?? throw new InvalidOperationException("User not found");
            bool self = user.Id == auth.UserId;
            if (self && (f.Role != UserRole.Admin || !f.IsActive)) return "You cannot remove your own admin role or disable your own account.";
            if (user.Role == UserRole.Admin && (f.Role != UserRole.Admin || !f.IsActive) && !await OtherActiveAdminExists(db, user.Id))
                return "There must be at least one active administrator.";
            user.FullName = f.FullName; user.Username = f.Username; user.Role = f.Role; user.IsActive = f.IsActive;
            if (!string.IsNullOrEmpty(f.Password)) user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(f.Password);
        }
        AuditService.Add(db, auth.UserId, auth.UserName, isNew ? "Add user" : "Edit user", $"{f.FullName} ({f.Username}, {f.Role})");
        await db.SaveChangesAsync();
        auth.RefreshCurrentUser(user);
        return null;
    }

    public async Task<string?> ResetPasswordAsync(int id, string newPassword)
    {
        if (!auth.Can(Perm.Users)) return "Only an administrator can reset passwords.";
        if (newPassword.Length < 6) return "Password must be at least 6 characters.";
        await using var db = await dbf.CreateDbContextAsync();
        var u = await db.Users.FindAsync(id);
        if (u == null) return "User not found.";
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        AuditService.Add(db, auth.UserId, auth.UserName, "Reset password", u.FullName);
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> ToggleActiveAsync(int id)
    {
        if (!auth.Can(Perm.Users)) return "Only an administrator can manage users.";
        if (id == auth.UserId) return "You cannot disable your own account.";
        await using var db = await dbf.CreateDbContextAsync();
        var u = await db.Users.FindAsync(id);
        if (u == null) return "User not found.";
        if (u.IsActive && u.Role == UserRole.Admin && !await OtherActiveAdminExists(db, id))
            return "There must be at least one active administrator.";
        u.IsActive = !u.IsActive;
        AuditService.Add(db, auth.UserId, auth.UserName, u.IsActive ? "Enable user" : "Disable user", u.FullName);
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> DeleteAsync(int id)
    {
        if (!auth.Can(Perm.Users)) return "Only an administrator can manage users.";
        if (id == auth.UserId) return "You cannot delete your own account.";
        await using var db = await dbf.CreateDbContextAsync();
        var u = await db.Users.FindAsync(id);
        if (u == null) return "User not found.";
        bool hasHistory = await db.Sales.AnyAsync(s => s.CashierId == id) || await db.StockMovements.AnyAsync(m => m.UserId == id)
                          || await db.Returns.AnyAsync(r => r.CreatedById == id || r.ProcessedById == id);
        if (hasHistory) return $"{u.FullName} has transaction history. Disable the account instead so the records stay complete.";
        if (u.Role == UserRole.Admin && !await OtherActiveAdminExists(db, id)) return "There must be at least one active administrator.";
        db.Users.Remove(u);
        AuditService.Add(db, auth.UserId, auth.UserName, "Delete user", u.FullName);
        await db.SaveChangesAsync();
        return null;
    }

    private static Task<bool> OtherActiveAdminExists(AppDbContext db, int exceptId) =>
        db.Users.AnyAsync(u => u.Id != exceptId && u.Role == UserRole.Admin && u.IsActive);
}
