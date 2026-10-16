using System.Drawing;
using WinForms = System.Windows.Forms;

namespace FinanceApp.Desktop.Tray;

/// <summary>The app lives here: quick entry on click, a small menu, unobtrusive notifications.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _quickEntryItem;
    private readonly Icon _image;

    public TrayIcon(Action openMain, Action openQuickEntry, Action openSettings, Action openDataFolder, Action exit)
    {
        _image = CreateIcon();
        _quickEntryItem = new WinForms.ToolStripMenuItem("Быстрый ввод", null, (_, _) => openQuickEntry());

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem("Открыть FinanceApp", null, (_, _) => openMain())
        {
            Font = new Font(WinForms.Control.DefaultFont, System.Drawing.FontStyle.Bold),
        });
        menu.Items.Add(_quickEntryItem);
        menu.Items.Add("Настройки…", null, (_, _) => openSettings());
        menu.Items.Add("Открыть папку с данными", null, (_, _) => openDataFolder());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = _image,
            Text = "FinanceApp",
            ContextMenuStrip = menu,
            Visible = true,
        };
        // Left click — quick entry (the main use); the full window is in the menu.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                openQuickEntry();
            }
        };
    }

    /// <summary>Shows the working hotkey in the menu and tooltip, or that there is none.</summary>
    public void SetHotKey(string? hotKey)
    {
        _quickEntryItem.ShortcutKeyDisplayString = hotKey ?? string.Empty;
        SetStatus(hotKey is null
            ? "FinanceApp — горячая клавиша не работает, клик для ввода"
            : $"FinanceApp — быстрый ввод: {hotKey}");
    }

    /// <summary>Tooltip is limited to 127 characters by Windows.</summary>
    public void SetStatus(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void Notify(string title, string text, bool isWarning = false) =>
        _icon.ShowBalloonTip(5000, title, text, isWarning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>The app icon (Assets\FinanceApp.ico) at the tray size for the current DPI.</summary>
    private static Icon CreateIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/FinanceApp.ico"))
            ?? throw new InvalidOperationException("Значок приложения не найден.");
        using var stream = resource.Stream;
        return new Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }
}
