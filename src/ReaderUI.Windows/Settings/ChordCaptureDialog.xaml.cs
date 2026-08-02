using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using Aura.Abstractions.Input;
using Aura.Input.Gestures;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Aura.UI.Settings;

[SupportedOSPlatform("windows6.1")]
public partial class ChordCaptureDialog : Window
{
    /// <summary>The chord captured by the user. Null until OK / Clear.</summary>
    public string? CapturedChord { get; private set; }

    /// <summary>True if the user clicked Clear (intent: remove this binding).</summary>
    public bool Cleared { get; private set; }

    private KeyChord? _pending;

    public ChordCaptureDialog()
    {
        InitializeComponent();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Escape cancels — consume so the IsCancel button doesn't double-fire.
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        var realKey = e.Key == Key.System ? e.SystemKey : e.Key;

        // Skip pure modifier presses; we wait until a real key arrives so the
        // user can build up the modifier set first.
        if (IsBareModifier(realKey))
        {
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(realKey);
        var modifiers = ReadModifiers();
        var chord = new KeyChord(vk, modifiers);
        _pending = chord;

        var formatted = KeyChordParser.Format(chord);
        PreviewText.Text = formatted;
        OkButton.IsEnabled = true;
        e.Handled = true;
    }

    private static InputModifiers ReadModifiers()
    {
        var mods = InputModifiers.None;
        var k = Keyboard.Modifiers;
        if ((k & ModifierKeys.Shift) != 0)
        {
            mods |= InputModifiers.Shift;
        }
        if ((k & ModifierKeys.Control) != 0)
        {
            mods |= InputModifiers.Control;
        }
        if ((k & ModifierKeys.Alt) != 0)
        {
            mods |= InputModifiers.Alt;
        }
        if ((k & ModifierKeys.Windows) != 0)
        {
            mods |= InputModifiers.Win;
        }
        // Reader (Insert / CapsLock) doesn't go through ModifierKeys; we read it
        // from key state directly.
        if (IsKeyDown(0x2D /* VK_INSERT */) || IsKeyDown(0x14 /* VK_CAPITAL */))
        {
            mods |= InputModifiers.Reader;
        }
        return mods;
    }

    private static bool IsBareModifier(Key key) => key
        is Key.LeftShift or Key.RightShift
        or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin
        or Key.System // alt-down arrives as System
        or Key.Insert or Key.Capital;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is { } chord)
        {
            CapturedChord = KeyChordParser.Format(chord);
            DialogResult = true;
            Close();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Cleared = true;
        CapturedChord = string.Empty;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
