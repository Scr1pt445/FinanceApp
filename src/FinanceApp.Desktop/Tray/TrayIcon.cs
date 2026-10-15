using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace FinanceApp.Desktop.Tray;

/// <summary>The app lives here: quick entry on click, a small menu, unobtrusive notifications.</summary>
internal sealed partial class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _quickEntryItem;
    private readonly Icon _image;

    public TrayIcon(Action openQuickEntry, Action openHotKeySettings, Action openDataFolder, Action exit)
    {
        _image = CreateIcon();
        _quickEntryItem = new WinForms.ToolStripMenuItem("Быстрый ввод", null, (_, _) => openQuickEntry())
        {
            Font = new Font(WinForms.Control.DefaultFont, System.Drawing.FontStyle.Bold),
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_quickEntryItem);
        menu.Items.Add("Горячая клавиша…", null, (_, _) => openHotKeySettings());
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

    /// <summary>A purple rounded square with «₽» — drawn at runtime, no binary assets needed.</summary>
    private static Icon CreateIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var path = new GraphicsPath();
            const int radius = 8;
            path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
            path.AddArc(size - radius * 2 - 1, 0, radius * 2, radius * 2, 270, 90);
            path.AddArc(size - radius * 2 - 1, size - radius * 2 - 1, radius * 2, radius * 2, 0, 90);
            path.AddArc(0, size - radius * 2 - 1, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x94, 0x46, 0xF8));
            graphics.FillPath(fill, path);
            using var font = new Font("Segoe UI", 18, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString("₽", font, Brushes.White, new RectangleF(0, 1, size, size), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);
}
