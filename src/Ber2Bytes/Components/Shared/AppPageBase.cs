using Ber2Bytes.Services;
using Microsoft.AspNetCore.Components;

namespace Ber2Bytes.Components.Shared;

/// <summary>
/// Base class for every page after login. It sends signed-out users to the login screen and
/// blocks pages the current role may not open. Pages use: @inherits AppPageBase
/// and override <see cref="Permission"/>.
/// </summary>
public abstract class AppPageBase : ComponentBase
{
    [Inject] protected AuthService Auth { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected ToastService Toast { get; set; } = default!;
    [Inject] protected ConfirmService Confirm { get; set; } = default!;
    [Inject] protected SettingsService Settings { get; set; } = default!;

    /// <summary>The permission needed to open this page (null = any signed-in user).</summary>
    protected virtual string? Permission => null;

    /// <summary>False while redirecting; pages should render nothing in that case.</summary>
    protected bool Allowed { get; private set; }

    protected override void OnInitialized()
    {
        if (!Auth.IsLoggedIn)
        {
            Nav.NavigateTo("/login");
            return;
        }
        if (Permission != null && !Auth.Can(Permission))
        {
            Toast.Error("Your role doesn't have access to that page.");
            Nav.NavigateTo("/");
            return;
        }
        Allowed = true;
    }

    /// <summary>Shows an error toast when <paramref name="error"/> is not null. Returns true when there was no error.</summary>
    protected bool Ok(string? error, string? successMessage = null)
    {
        if (error != null)
        {
            Toast.Error(error);
            return false;
        }
        if (successMessage != null) Toast.Success(successMessage);
        return true;
    }
}
