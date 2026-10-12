using Ber2Bytes.Data;
using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Services;

/// <summary>
/// Every action in the app is checked against one of these permissions.
/// Admin has all of them. Staff has only the ones in <see cref="AuthService.StaffPermissions"/>.
/// </summary>
public static class Perm
{
    public const string Dashboard = "dashboard";
    public const string Pos = "pos";
    public const string ProductsView = "products.view";
    public const string ProductsManage = "products.manage";     // add / edit / archive / delete
    public const string ViewCost = "products.cost";             // see cost price and profit
    public const string StockView = "stock.view";
    public const string StockIn = "stock.in";
    public const string StockAdjust = "stock.adjust";
    public const string SalesView = "sales.view";
    public const string SalesViewAll = "sales.all";             // staff only sees their own sales
    public const string SalesVoid = "sales.void";
    public const string Categories = "categories";
    public const string SuppliersView = "suppliers.view";
    public const string SuppliersManage = "suppliers.manage";
    public const string Returns = "returns";
    public const string ReturnsProcess = "returns.process";     // approve / reject
    public const string Reports = "reports";
    public const string ReportLowStock = "report.lowstock";
    public const string ReportBusiness = "report.business";     // sales, top sellers, dead stock, valuation
    public const string Alerts = "alerts";
    public const string Users = "users";
    public const string Audit = "audit";
    public const string Settings = "settings";
}

public enum LoginResult { Success, InvalidCredentials, Disabled, LockedOut }

public class AuthService(IDbContextFactory<AppDbContext> dbf, AuditService audit, LocalPrefs prefs)
{
    public static readonly HashSet<string> StaffPermissions =
    [
        Perm.Dashboard, Perm.Pos, Perm.ProductsView, Perm.StockView, Perm.StockIn,
        Perm.SalesView, Perm.SuppliersView, Perm.Returns, Perm.Reports, Perm.ReportLowStock, Perm.Alerts
    ];

    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromSeconds(60);
    private int _failedAttempts;
    private DateTime? _lockedUntil;

    public User? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser != null;
    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;
    public int UserId => CurrentUser?.Id ?? 0;
    public string UserName => CurrentUser?.FullName ?? "";
    public event Action? Changed;

    public bool Can(string permission) =>
        CurrentUser != null && (CurrentUser.Role == UserRole.Admin || StaffPermissions.Contains(permission));

    public int LockSecondsLeft =>
        _lockedUntil is { } t && t > DateTime.Now ? (int)Math.Ceiling((t - DateTime.Now).TotalSeconds) : 0;

    public async Task<LoginResult> LoginAsync(string username, string password, bool rememberMe)
    {
        if (LockSecondsLeft > 0) return LoginResult.LockedOut;

        username = username.Trim().ToLowerInvariant();
        await using var db = await dbf.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _failedAttempts++;
            if (_failedAttempts >= MaxAttempts)
            {
                _lockedUntil = DateTime.Now.Add(LockTime);
                _failedAttempts = 0;
                await audit.LogAsync(null, username, "Login locked", $"Too many failed attempts for '{username}'");
                return LoginResult.LockedOut;
            }
            return LoginResult.InvalidCredentials;
        }
        if (!user.IsActive) return LoginResult.Disabled;

        _failedAttempts = 0;
        user.LastLoginAt = DateTime.Now;
        await db.SaveChangesAsync();
        CurrentUser = user;

        prefs.RememberedUsername = rememberMe ? user.Username : null;
        prefs.Save();

        await audit.LogAsync(user.Id, user.FullName, "Login", "Signed in");
        Changed?.Invoke();
        return LoginResult.Success;
    }

    public async Task LogoutAsync()
    {
        if (CurrentUser != null)
            await audit.LogAsync(CurrentUser.Id, CurrentUser.FullName, "Logout", "Signed out");
        CurrentUser = null;
        Changed?.Invoke();
    }

    /// <summary>Lets any signed-in user change their own password.</summary>
    public async Task<string?> ChangeOwnPasswordAsync(string current, string newPassword)
    {
        if (CurrentUser == null) return "You are not signed in.";
        if (newPassword.Length < 6) return "New password must be at least 6 characters.";
        await using var db = await dbf.CreateDbContextAsync();
        var user = await db.Users.FindAsync(CurrentUser.Id);
        if (user == null || !BCrypt.Net.BCrypt.Verify(current, user.PasswordHash)) return "Your current password is incorrect.";
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await db.SaveChangesAsync();
        await audit.LogAsync(user.Id, user.FullName, "Change password", "Changed own password");
        return null;
    }

    /// <summary>Called after an admin edits their own account so the sidebar shows the new name.</summary>
    public void RefreshCurrentUser(User updated)
    {
        if (CurrentUser?.Id == updated.Id)
        {
            CurrentUser = updated;
            Changed?.Invoke();
        }
    }
}
