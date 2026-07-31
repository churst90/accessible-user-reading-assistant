using OpenReader.Abstractions.Input;
using OpenReader.Input.Commands;

namespace OpenReader.Input.Gestures;

/// <summary>Choice of default keyboard layout. Mirrors NVDA's desktop / laptop split.</summary>
public enum KeyboardLayout
{
    /// <summary>Insert as Reader modifier; numpad drives review (NumLock ON).</summary>
    Desktop = 0,

    /// <summary>CapsLock as Reader modifier; main arrow cluster drives review.</summary>
    Laptop = 1,
}

/// <summary>
/// Built-in default chord bindings, parameterized by keyboard layout. The
/// numpad row mapping and shared chords mirror NVDA's defaults so users
/// switching from NVDA see expected behavior without rebinding.
/// </summary>
public static class GestureBindings
{
    // Main keyboard
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_HOME = 0x24;
    private const int VK_END = 0x23;
    private const int VK_TAB = 0x09;
    private const int VK_A = 0x41;
    private const int VK_F = 0x46;
    private const int VK_L = 0x4C;
    private const int VK_N = 0x4E;
    private const int VK_O = 0x4F;
    private const int VK_P = 0x50;
    private const int VK_Q = 0x51;
    private const int VK_S = 0x53;
    private const int VK_T = 0x54;
    private const int VK_OEM_PERIOD = 0xBE;
    private const int VK_CONTROL = 0x11;
    private const int VK_1 = 0x31;
    private const int VK_F1 = 0x70;
    private const int VK_F12 = 0x7B;

    // Numpad (NumLock ON sends these)
    private const int VK_NUMPAD1 = 0x61;
    private const int VK_NUMPAD2 = 0x62;
    private const int VK_NUMPAD3 = 0x63;
    private const int VK_NUMPAD4 = 0x64;
    private const int VK_NUMPAD5 = 0x65;
    private const int VK_NUMPAD6 = 0x66;
    private const int VK_NUMPAD7 = 0x67;
    private const int VK_NUMPAD8 = 0x68;
    private const int VK_NUMPAD9 = 0x69;
    private const int VK_ADD = 0x6B;     // Numpad +
    private const int VK_DECIMAL = 0x6E; // Numpad .

    /// <summary>Apply the built-in defaults for the given <paramref name="layout"/>.</summary>
    public static void ApplyDefaults(GestureMap map, KeyboardLayout layout = KeyboardLayout.Desktop)
    {
        ArgumentNullException.ThrowIfNull(map);

        ApplyShared(map);
        switch (layout)
        {
            case KeyboardLayout.Laptop:
                ApplyLaptop(map);
                break;
            case KeyboardLayout.Desktop:
            default:
                ApplyDesktop(map);
                break;
        }
    }

    /// <summary>Replace existing bindings with the layout's defaults; clears non-default bindings.</summary>
    public static void Reset(GestureMap map, KeyboardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var chord in map.Snapshot().Keys)
        {
            map.Unbind(chord);
        }
        ApplyDefaults(map, layout);
    }

    private static void ApplyShared(GestureMap map)
    {
        // Stop speech: bare Ctrl is observed-but-passed-through (see InputSuppressionPolicy).
        map.Bind(new KeyChord(VK_CONTROL, InputModifiers.Control), ReaderCommand.StopSpeech);

        // Reporting and meta. NVDA: Insert+Tab = report current focus,
        // Insert+T = report title. Insert+F is OpenReader-specific (legacy
        // alias for ReportFocus while we don't ship report-formatting yet).
        map.Bind(new KeyChord(VK_TAB, InputModifiers.Reader), ReaderCommand.ReportFocus);
        map.Bind(new KeyChord(VK_F, InputModifiers.Reader), ReaderCommand.ReportFocus);
        map.Bind(new KeyChord(VK_T, InputModifiers.Reader), ReaderCommand.ReportTitle);

        // Time and date: Reader+F12 reads time; double-press converts to date in
        // the host (see DoubleTapDetector). The map only binds the single press.
        map.Bind(new KeyChord(VK_F12, InputModifiers.Reader), ReaderCommand.ReportTime);

        // Punctuation level cycle (NVDA: Insert+P).
        map.Bind(new KeyChord(VK_P, InputModifiers.Reader), ReaderCommand.CyclePunctuationLevel);

        // Keyboard help (NVDA: Insert+1) and documentation (Reader+F1, custom).
        map.Bind(new KeyChord(VK_1, InputModifiers.Reader), ReaderCommand.ToggleKeyboardHelp);
        map.Bind(new KeyChord(VK_F1, InputModifiers.Reader), ReaderCommand.OpenDocumentation);

        // Settings (Reader+N mirrors NVDA's "open menu"; Reader+O kept as
        // legacy direct-access alias). Quit (Reader+Q) matches NVDA.
        map.Bind(new KeyChord(VK_N, InputModifiers.Reader), ReaderCommand.OpenSettings);
        map.Bind(new KeyChord(VK_O, InputModifiers.Reader), ReaderCommand.OpenSettings);
        map.Bind(new KeyChord(VK_Q, InputModifiers.Reader), ReaderCommand.OpenExitDialog);

        // Synthesizer selection. Ctrl+Reader+S (so plain Ctrl+S still saves
        // in the focused app — chord matching is exact on modifiers).
        map.Bind(new KeyChord(VK_S, InputModifiers.Reader | InputModifiers.Control), ReaderCommand.OpenSynthesizerDialog);

        // Read-current-line and say-all-from-cursor are bound on the main
        // letter row in both layouts so muscle memory carries between them
        // (NVDA convention). The layout-specific arrow / numpad chords stay
        // in the per-layout Apply methods below.
        map.Bind(new KeyChord(VK_L, InputModifiers.Reader), ReaderCommand.ReadLine);
        map.Bind(new KeyChord(VK_A, InputModifiers.Reader), ReaderCommand.SayAllFromCursor);
    }

    /// <summary>
    /// NVDA laptop layout: Reader (CapsLock) modifier with the main arrow
    /// cluster driving review. Periods and shifted periods cover the
    /// "current character / word / line" reads NVDA expects.
    /// </summary>
    private static void ApplyLaptop(GestureMap map)
    {
        // Character row.
        map.Bind(new KeyChord(VK_LEFT, InputModifiers.Reader), ReaderCommand.ReadPreviousCharacter);
        map.Bind(new KeyChord(VK_RIGHT, InputModifiers.Reader), ReaderCommand.ReadNextCharacter);
        map.Bind(new KeyChord(VK_OEM_PERIOD, InputModifiers.Reader), ReaderCommand.ReadCharacter);

        // Word row.
        map.Bind(new KeyChord(VK_LEFT, InputModifiers.Reader | InputModifiers.Control), ReaderCommand.ReadPreviousWord);
        map.Bind(new KeyChord(VK_RIGHT, InputModifiers.Reader | InputModifiers.Control), ReaderCommand.ReadNextWord);
        map.Bind(new KeyChord(VK_OEM_PERIOD, InputModifiers.Reader | InputModifiers.Control), ReaderCommand.ReadWord);

        // Line row.
        map.Bind(new KeyChord(VK_UP, InputModifiers.Reader), ReaderCommand.ReadPreviousLine);
        map.Bind(new KeyChord(VK_DOWN, InputModifiers.Reader), ReaderCommand.ReadNextLine);
        map.Bind(new KeyChord(VK_OEM_PERIOD, InputModifiers.Reader | InputModifiers.Shift), ReaderCommand.ReadLine);

        // Say-all from cursor (NVDA laptop: NVDA+Shift+A) and from beginning (custom).
        map.Bind(new KeyChord(VK_A, InputModifiers.Reader | InputModifiers.Shift), ReaderCommand.SayAllFromCursor);
        map.Bind(new KeyChord(VK_A, InputModifiers.Reader | InputModifiers.Shift | InputModifiers.Control), ReaderCommand.SayAll);

        // Review jump-to bookends (Reader+Shift+Home/End).
        map.Bind(new KeyChord(VK_HOME, InputModifiers.Reader | InputModifiers.Shift), ReaderCommand.ReviewMoveToTop);
        map.Bind(new KeyChord(VK_END, InputModifiers.Reader | InputModifiers.Shift), ReaderCommand.ReviewMoveToBottom);
    }

    /// <summary>
    /// NVDA desktop layout: numpad drives review with the standard NVDA
    /// numpad cluster (1/2/3 character, 4/5/6 word, 7/8/9 line). Insert+Down
    /// is say-all-from-cursor; Numpad+ is the same (NVDA convention).
    /// </summary>
    private static void ApplyDesktop(GestureMap map)
    {
        // NVDA numpad cluster (NumLock ON).
        //
        //   7 PrevLine  8 CurLine    9 NextLine
        //   4 PrevWord  5 CurWord    6 NextWord
        //   1 PrevChar  2 CurChar    3 NextChar
        //
        //   Numpad+ Say all from cursor
        //   Numpad. Move review to focus
        map.Bind(new KeyChord(VK_NUMPAD1, InputModifiers.None), ReaderCommand.ReadPreviousCharacter);
        map.Bind(new KeyChord(VK_NUMPAD2, InputModifiers.None), ReaderCommand.ReadCharacter);
        map.Bind(new KeyChord(VK_NUMPAD3, InputModifiers.None), ReaderCommand.ReadNextCharacter);
        map.Bind(new KeyChord(VK_NUMPAD4, InputModifiers.None), ReaderCommand.ReadPreviousWord);
        map.Bind(new KeyChord(VK_NUMPAD5, InputModifiers.None), ReaderCommand.ReadWord);
        map.Bind(new KeyChord(VK_NUMPAD6, InputModifiers.None), ReaderCommand.ReadNextWord);
        map.Bind(new KeyChord(VK_NUMPAD7, InputModifiers.None), ReaderCommand.ReadPreviousLine);
        map.Bind(new KeyChord(VK_NUMPAD8, InputModifiers.None), ReaderCommand.ReadLine);
        map.Bind(new KeyChord(VK_NUMPAD9, InputModifiers.None), ReaderCommand.ReadNextLine);

        // Top / bottom of review: NVDA = Shift+Numpad7 / Shift+Numpad1.
        map.Bind(new KeyChord(VK_NUMPAD7, InputModifiers.Shift), ReaderCommand.ReviewMoveToTop);
        map.Bind(new KeyChord(VK_NUMPAD1, InputModifiers.Shift), ReaderCommand.ReviewMoveToBottom);

        // Say-all (NVDA: NVDA+Numpad+ AND NVDA+Down for desktop).
        map.Bind(new KeyChord(VK_ADD, InputModifiers.Reader), ReaderCommand.SayAllFromCursor);
        map.Bind(new KeyChord(VK_DOWN, InputModifiers.Reader), ReaderCommand.SayAllFromCursor);
        // From-beginning is OpenReader-specific.
        map.Bind(new KeyChord(VK_DOWN, InputModifiers.Reader | InputModifiers.Shift), ReaderCommand.SayAll);

        // Review move to focus (NVDA: NVDA+Numpad. ish; we keep Reader+. too).
        map.Bind(new KeyChord(VK_DECIMAL, InputModifiers.Reader), ReaderCommand.ReviewMoveToFocus);
        map.Bind(new KeyChord(VK_OEM_PERIOD, InputModifiers.Reader), ReaderCommand.ReviewMoveToFocus);

        // NVDA: Insert+Up reads the current line. Bind for parity.
        map.Bind(new KeyChord(VK_UP, InputModifiers.Reader), ReaderCommand.ReadLine);
    }
}
