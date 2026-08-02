using System.Runtime.Versioning;

namespace Aura.UI.Tray;

/// <summary>
/// System-tray icon hosting Aura's status indicator and quick menu.
/// </summary>
/// <remarks>
/// <para>
/// Uses WinForms <see cref="System.Windows.Forms.NotifyIcon"/>. Construct on
/// the UI dispatcher thread; the menu callbacks fire on the same thread, so
/// dispatch back to background workers as needed.
/// </para>
/// <para>
/// The icon is generated programmatically (a simple "OR" glyph in green when
/// enabled, gray when disabled) so no .ico asset is required to ship. A
/// future iteration can swap in a designer-supplied icon.
/// </para>
/// <para>
/// <b>Menu announcements.</b> WinForms <c>ToolStripMenuItem</c>s have weak UIA
/// focus-event support — third-party screen readers (us included) often miss
/// them. We bridge that gap by polling the menu's <c>SelectedItem</c> while
/// it's open and routing the label through the optional <c>announce</c>
/// callback.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows6.1")]
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notify;
    private readonly System.Windows.Forms.ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _toggleItem;
    private readonly Action<string>? _announce;
    private bool _enabled = true;
    private bool _disposed;

    public TrayIcon(
        Action onOpenSettings,
        Action onOpenDocumentation,
        Action onToggleEnabled,
        Action onExit,
        Action<string>? announce = null)
    {
        ArgumentNullException.ThrowIfNull(onOpenSettings);
        ArgumentNullException.ThrowIfNull(onOpenDocumentation);
        ArgumentNullException.ThrowIfNull(onToggleEnabled);
        ArgumentNullException.ThrowIfNull(onExit);
        _announce = announce;

        var menu = new AnnouncingContextMenu(_announce);
        _statusItem = new System.Windows.Forms.ToolStripMenuItem("Aura: enabled") { Enabled = false };
        _toggleItem = new System.Windows.Forms.ToolStripMenuItem("Toggle on/off");
        _toggleItem.Click += (_, _) => onToggleEnabled();

        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => onOpenSettings();

        var docsItem = new System.Windows.Forms.ToolStripMenuItem("Documentation");
        docsItem.Click += (_, _) => onOpenDocumentation();

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => onExit();

        menu.Items.Add(_statusItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(docsItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notify = new System.Windows.Forms.NotifyIcon
        {
            Icon = BuildIcon(_enabled),
            Text = "Aura",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notify.DoubleClick += (_, _) => onOpenSettings();
    }

    /// <summary>Update the icon and tooltip to reflect enabled / disabled state.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
        {
            return;
        }
        _enabled = enabled;
        _notify.Icon?.Dispose();
        _notify.Icon = BuildIcon(enabled);
        _notify.Text = enabled ? "Aura" : "Aura (off)";
        _statusItem.Text = enabled ? "Aura: enabled" : "Aura: disabled";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _notify.Visible = false;
        _notify.Icon?.Dispose();
        _notify.Dispose();
    }

    /// <summary>
    /// <see cref="System.Windows.Forms.ContextMenuStrip"/> that announces each
    /// item as the keyboard or mouse moves selection. WinForms ToolStrip
    /// controls have weak UIA focus support — third-party screen readers (us
    /// included) miss the menu-item focus events. We bridge the gap by polling
    /// <c>SelectedItem</c> while the menu is open and announcing on change.
    /// </summary>
    private sealed class AnnouncingContextMenu : System.Windows.Forms.ContextMenuStrip
    {
        private readonly Action<string>? _announce;
        private readonly System.Windows.Forms.Timer _poll;
        private System.Windows.Forms.ToolStripItem? _lastAnnounced;

        public AnnouncingContextMenu(Action<string>? announce)
        {
            _announce = announce;
            _poll = new System.Windows.Forms.Timer { Interval = 50 };
            _poll.Tick += (_, _) => CheckSelection();

            if (_announce is not null)
            {
                Opened += (_, _) =>
                {
                    _announce("Aura menu");
                    _lastAnnounced = null;
                    _poll.Start();
                };
                Closed += (_, _) =>
                {
                    _poll.Stop();
                    _lastAnnounced = null;
                };
            }
        }

        private void CheckSelection()
        {
            if (_announce is null)
            {
                return;
            }
            var current = FindSelectedItem();
            if (current is null || ReferenceEquals(current, _lastAnnounced))
            {
                return;
            }
            _lastAnnounced = current;
            var label = current.Text;
            if (!string.IsNullOrEmpty(label))
            {
                _announce(label);
            }
        }

        private System.Windows.Forms.ToolStripItem? FindSelectedItem()
        {
            foreach (System.Windows.Forms.ToolStripItem item in Items)
            {
                if (item.Selected)
                {
                    return item;
                }
            }
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _poll.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static System.Drawing.Icon BuildIcon(bool enabled)
    {
        using var bmp = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var bg = new System.Drawing.SolidBrush(enabled
                ? System.Drawing.Color.FromArgb(40, 130, 80)
                : System.Drawing.Color.FromArgb(110, 110, 110));
            g.FillEllipse(bg, 0, 0, 15, 15);
            using var fg = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            using var font = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            var size = g.MeasureString("OR", font);
            g.DrawString("OR", font, fg, (16 - size.Width) / 2f, (16 - size.Height) / 2f);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
