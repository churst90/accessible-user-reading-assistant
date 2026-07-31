using OpenReader.Abstractions.Input;

namespace OpenReader.Input.Gestures;

/// <summary>
/// Parses user-friendly chord strings ("Reader+Ctrl+Right", "NumPad4",
/// "Insert+O") into <see cref="KeyChord"/>s and back. Used by
/// <c>InputConfig.KeyBindings</c> and the rebind UI.
/// </summary>
/// <remarks>
/// <para>
/// Format: tokens separated by <c>+</c>, case-insensitive, whitespace-tolerant.
/// Modifiers (any subset, in any order): <c>Reader</c>, <c>Insert</c>,
/// <c>CapsLock</c>, <c>Ctrl</c>/<c>Control</c>, <c>Shift</c>, <c>Alt</c>,
/// <c>Win</c>. The final token is the key.
/// </para>
/// <para>
/// <c>Insert</c> and <c>CapsLock</c> as modifiers are aliases for
/// <c>Reader</c> — what they actually do depends on the active layout. This
/// keeps user config readable across users on different layouts.
/// </para>
/// </remarks>
public static class KeyChordParser
{
    public static bool TryParse(string text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = InputModifiers.None;
        int? keyCode = null;
        for (var i = 0; i < parts.Length; i++)
        {
            var token = parts[i];
            // Anything that's a known modifier keyword is added to the mask;
            // the final non-modifier token is the key.
            if (TryParseModifier(token, out var mod))
            {
                modifiers |= mod;
                continue;
            }
            if (i != parts.Length - 1)
            {
                // Modifier token wasn't recognised — abort.
                return false;
            }
            if (!TryParseKey(token, out var vk))
            {
                return false;
            }
            keyCode = vk;
        }

        if (!keyCode.HasValue)
        {
            return false;
        }
        chord = new KeyChord(keyCode.Value, modifiers);
        return true;
    }

    public static string Format(KeyChord chord)
    {
        var parts = new List<string>(4);
        if ((chord.Modifiers & InputModifiers.Reader) != 0)
        {
            parts.Add("Reader");
        }
        if ((chord.Modifiers & InputModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((chord.Modifiers & InputModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }
        if ((chord.Modifiers & InputModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }
        if ((chord.Modifiers & InputModifiers.Win) != 0)
        {
            parts.Add("Win");
        }
        parts.Add(KeyName(chord.KeyCode));
        return string.Join('+', parts);
    }

    private static bool TryParseModifier(string token, out InputModifiers mod)
    {
        mod = token.ToUpperInvariant() switch
        {
            "READER" or "INSERT" or "CAPSLOCK" or "CAPS" => InputModifiers.Reader,
            "CTRL" or "CONTROL" => InputModifiers.Control,
            "ALT" => InputModifiers.Alt,
            "SHIFT" => InputModifiers.Shift,
            "WIN" or "WINDOWS" or "META" => InputModifiers.Win,
            _ => InputModifiers.None,
        };
        return mod != InputModifiers.None;
    }

    private static bool TryParseKey(string token, out int vk)
    {
        var upper = token.ToUpperInvariant();
        if (NameToVk.TryGetValue(upper, out vk))
        {
            return true;
        }
        if (upper.Length == 1)
        {
            var ch = upper[0];
            if (ch is >= 'A' and <= 'Z')
            {
                vk = ch;
                return true;
            }
            if (ch is >= '0' and <= '9')
            {
                vk = ch;
                return true;
            }
        }
        if (upper.StartsWith('F') && int.TryParse(upper.AsSpan(1), out var f) && f is >= 1 and <= 24)
        {
            vk = 0x70 + (f - 1);
            return true;
        }
        if (upper.StartsWith("NUMPAD", StringComparison.Ordinal) && int.TryParse(upper.AsSpan(6), out var n) && n is >= 0 and <= 9)
        {
            vk = 0x60 + n;
            return true;
        }
        vk = 0;
        return false;
    }

    private static string KeyName(int vk)
    {
        if (VkToName.TryGetValue(vk, out var name))
        {
            return name;
        }
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)vk).ToString();
        }
        if (vk is >= 0x70 and <= 0x87)
        {
            return $"F{vk - 0x70 + 1}";
        }
        if (vk is >= 0x60 and <= 0x69)
        {
            return $"NumPad{vk - 0x60}";
        }
        return $"VK_{vk:X2}";
    }

    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E,
        ["SPACE"] = 0x20,
        ["ENTER"] = 0x0D,
        ["RETURN"] = 0x0D,
        ["TAB"] = 0x09,
        ["ESCAPE"] = 0x1B,
        ["ESC"] = 0x1B,
        ["BACKSPACE"] = 0x08,
        ["CAPSLOCK"] = 0x14,
        ["NUMLOCK"] = 0x90,
        ["SCROLLLOCK"] = 0x91,
        ["PRINTSCREEN"] = 0x2C,
        ["PAUSE"] = 0x13,
        ["PERIOD"] = 0xBE,
        ["COMMA"] = 0xBC,
        ["SLASH"] = 0xBF,
        ["BACKSLASH"] = 0xDC,
        ["SEMICOLON"] = 0xBA,
        ["QUOTE"] = 0xDE,
        ["MINUS"] = 0xBD,
        ["EQUALS"] = 0xBB,
        ["LEFTBRACKET"] = 0xDB,
        ["RIGHTBRACKET"] = 0xDD,
        ["GRAVE"] = 0xC0,
    };

    private static readonly Dictionary<int, string> VkToName = BuildVkToName();

    private static Dictionary<int, string> BuildVkToName()
    {
        var map = new Dictionary<int, string>();
        foreach (var (name, vk) in NameToVk)
        {
            // First name we see for a VK wins; aliases come after the canonical name in NameToVk.
            map.TryAdd(vk, Capitalize(name));
        }
        return map;
    }

    private static string Capitalize(string s)
    {
        if (s.Length == 0)
        {
            return s;
        }
        return char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }
}
