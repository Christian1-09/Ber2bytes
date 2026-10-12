using Microsoft.JSInterop;

namespace Ber2Bytes.Services;

public class HotkeyEvent(string key)
{
    public string Key { get; } = key;
    public bool Handled { get; set; }
}

/// <summary>
/// Receives F2 / F4 / F8 / F9 / Escape from app.js and passes them to the components
/// that subscribed. The newest subscriber gets the key first (e.g. an open dialog before the page).
/// </summary>
public class HotkeyService
{
    private readonly List<(Func<HotkeyEvent, Task> Handler, int Priority, long Order)> _handlers = [];
    private long _order;

    /// <summary>Higher priority runs first (confirm box 100, dialogs 50, pages 0); same priority → newest first.</summary>
    public IDisposable Subscribe(Func<HotkeyEvent, Task> handler, int priority = 0)
    {
        var entry = (handler, priority, ++_order);
        _handlers.Add(entry);
        return new Unsubscriber(() => _handlers.Remove(entry));
    }

    [JSInvokable]
    public async Task OnHotkey(string key)
    {
        var e = new HotkeyEvent(key);
        foreach (var h in _handlers.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Order).ToList())
        {
            await h.Handler(e);
            if (e.Handled) break;
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
