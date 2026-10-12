// Small JavaScript helpers called from Blazor through IJSRuntime ("b2b.xxx").
window.b2b = {
    focus: function (id, select) {
        const el = document.getElementById(id);
        if (el) { el.focus(); if (select && el.select) el.select(); }
    },

    click: function (id) {
        const el = document.getElementById(id);
        if (el) el.click();
    },

    scrollTop: function (selector) {
        const el = document.querySelector(selector);
        if (el) el.scrollTop = 0;
    },

    // Prints a piece of HTML (receipts) through a hidden iframe so only that part is printed.
    printHtml: function (title, html, css) {
        let frame = document.getElementById('print-frame');
        if (!frame) {
            frame = document.createElement('iframe');
            frame.id = 'print-frame';
            frame.style.cssText = 'position:fixed;width:0;height:0;border:0;right:0;bottom:0';
            document.body.appendChild(frame);
        }
        const doc = frame.contentWindow.document;
        doc.open();
        doc.write('<!doctype html><html><head><meta charset="utf-8"><title>' + title + '</title><style>' + css + '</style></head><body>' + html + '</body></html>');
        doc.close();
        setTimeout(function () { frame.contentWindow.focus(); frame.contentWindow.print(); }, 200);
    },

    // Keyboard shortcuts. Blazor registers a .NET object that receives the key names.
    _hotkeyRef: null,
    registerHotkeys: function (dotnetRef) { window.b2b._hotkeyRef = dotnetRef; },
    unregisterHotkeys: function () { window.b2b._hotkeyRef = null; }
};

document.addEventListener('keydown', function (e) {
    // Ctrl+K → global search
    if (e.ctrlKey && (e.key === 'k' || e.key === 'K')) {
        const s = document.getElementById('global-search');
        if (s) { e.preventDefault(); s.focus(); s.select(); }
        return;
    }
    const keys = ['F2', 'F4', 'F8', 'F9', 'Escape'];
    if (keys.indexOf(e.key) >= 0 && window.b2b._hotkeyRef) {
        if (e.key !== 'Escape') e.preventDefault();
        window.b2b._hotkeyRef.invokeMethodAsync('OnHotkey', e.key);
    }
});
