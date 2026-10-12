using System.Text.Json;
using Ber2Bytes.Helpers;

namespace Ber2Bytes.Services;

/// <summary>Raised whenever data changes so the sidebar badges and bell can refresh.</summary>
public class AppState
{
    public event Action? DataChanged;
    public void NotifyDataChanged() => DataChanged?.Invoke();
}

public enum ToastKind { Success, Error, Warning, Info }
public record ToastMessage(Guid Id, string Text, ToastKind Kind);

/// <summary>Small pop-up messages in the bottom-right corner.</summary>
public class ToastService
{
    public event Action? Changed;
    public List<ToastMessage> Items { get; } = [];

    public void Success(string text) => Show(text, ToastKind.Success);
    public void Error(string text) => Show(text, ToastKind.Error);
    public void Warning(string text) => Show(text, ToastKind.Warning);
    public void Info(string text) => Show(text, ToastKind.Info);

    public void Show(string text, ToastKind kind)
    {
        var t = new ToastMessage(Guid.NewGuid(), text, kind);
        Items.Add(t);
        Changed?.Invoke();
        _ = RemoveLater(t, kind == ToastKind.Error ? 5000 : 3500);
    }

    public void Dismiss(Guid id)
    {
        Items.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }

    private async Task RemoveLater(ToastMessage t, int ms)
    {
        await Task.Delay(ms);
        Dismiss(t.Id);
    }
}

public class ConfirmRequest
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string OkText { get; init; } = "Confirm";
    public bool Danger { get; init; } = true;
    /// <summary>When set, the dialog shows a text box with this label and the text must be filled in.</summary>
    public string? InputLabel { get; init; }
    public bool InputIsPassword { get; init; }
    /// <summary>When set, the user must type exactly this word to enable the OK button.</summary>
    public string? TypeToConfirm { get; init; }
    public TaskCompletionSource<string?> Result { get; } = new();
}

/// <summary>"Are you sure?" dialogs that can be awaited: if (await Confirm.Ask(...)) { ... }</summary>
public class ConfirmService
{
    public event Action? Changed;
    public ConfirmRequest? Current { get; private set; }

    public async Task<bool> Ask(string title, string message, string okText = "Confirm", bool danger = true)
        => await Show(new ConfirmRequest { Title = title, Message = message, OkText = okText, Danger = danger }) != null;

    /// <summary>Returns the typed text, or null if cancelled.</summary>
    public Task<string?> AskText(string title, string message, string inputLabel, string okText = "Confirm", bool danger = true, bool password = false)
        => Show(new ConfirmRequest { Title = title, Message = message, OkText = okText, Danger = danger, InputLabel = inputLabel, InputIsPassword = password });

    public async Task<bool> AskTyped(string title, string message, string word, string okText)
        => await Show(new ConfirmRequest { Title = title, Message = message, OkText = okText, TypeToConfirm = word }) != null;

    public Task<string?> Show(ConfirmRequest request)
    {
        Current?.Result.TrySetResult(null);
        Current = request;
        Changed?.Invoke();
        return request.Result.Task;
    }

    public void Close(string? value)
    {
        var c = Current;
        Current = null;
        Changed?.Invoke();
        c?.Result.TrySetResult(value);
    }
}

/// <summary>Per-computer preferences saved in prefs.json (e.g. "Remember me").</summary>
public class LocalPrefs
{
    public string? RememberedUsername { get; set; }
    public bool SidebarCollapsed { get; set; }

    public LocalPrefs()
    {
        try
        {
            if (File.Exists(AppPaths.PrefsFile))
            {
                var saved = JsonSerializer.Deserialize<PrefsData>(File.ReadAllText(AppPaths.PrefsFile));
                RememberedUsername = saved?.RememberedUsername;
                SidebarCollapsed = saved?.SidebarCollapsed ?? false;
            }
        }
        catch (Exception ex) { AppLog.Error("Could not read prefs", ex); }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.PrefsFile, JsonSerializer.Serialize(new PrefsData(RememberedUsername, SidebarCollapsed)));
        }
        catch (Exception ex) { AppLog.Error("Could not save prefs", ex); }
    }

    private record PrefsData(string? RememberedUsername, bool SidebarCollapsed);
}
