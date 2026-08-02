using System.Text;
using OpenReader.Abstractions.Input;
using OpenReader.Abstractions.Text;
using OpenReader.Diagnostics;
using Serilog;

namespace OpenReader.Input.Echo;

/// <summary>
/// Listens to an <see cref="IInputSource"/> and synthesizes speech announcements
/// for typed characters, completed words, modifier keys, and named navigation
/// keys, gated by <see cref="KeyEchoSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// The service emits via a host-supplied callback so it stays independent of
/// the speech pipeline. The host wires the callback to
/// <c>SpeechPipeline.Submit(SpeechRequest)</c> with reason
/// <c>SpeechReason.UserAnnouncement</c>.
/// </para>
/// <para>
/// Word echo is non-trivial: we accumulate typed characters into a small
/// buffer, and on a word-break key (Space, Enter, Tab, punctuation) we emit
/// the buffer and clear it. Backspace pops the last character. Navigation
/// keys (arrows / Home / End) clear the buffer because the caret has moved.
/// </para>
/// </remarks>
public sealed class KeyEchoService : IDisposable
{
    private const int VK_BACK = 0x08;
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_PRIOR = 0x21;
    private const int VK_NEXT = 0x22;
    private const int VK_END = 0x23;
    private const int VK_HOME = 0x24;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_INSERT = 0x2D;
    private const int VK_DELETE = 0x2E;
    private const int VK_F1 = 0x70;
    private const int VK_F12 = 0x7B;

    private readonly IInputSource _source;
    private readonly Action<string> _speak;
    private readonly Action? _onTypingActivity;
    private readonly StringBuilder _wordBuffer = new(64);
    private readonly object _gate = new();
    private readonly ILogger _log;
    private KeyEchoSettings _settings;
    private bool _started;
    private bool _disposed;

    public KeyEchoService(
        IInputSource source,
        Action<string> speak,
        KeyEchoSettings? initialSettings = null,
        Action? onTypingActivity = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _speak = speak ?? throw new ArgumentNullException(nameof(speak));
        _onTypingActivity = onTypingActivity;
        _settings = initialSettings ?? KeyEchoSettings.Defaults;
        _log = LoggerFactory.ForComponent("Input.KeyEcho");
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _source.RawInputReceived += OnRawInput;
    }

    public void Update(KeyEchoSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        lock (_gate)
        {
            _wordBuffer.Clear();
        }
    }

    public KeyEchoSettings CurrentSettings => _settings;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _source.RawInputReceived -= OnRawInput;
    }

    private void OnRawInput(object? sender, RawInput input)
    {
        if (input.Kind != InputEventKind.KeyDown)
        {
            return;
        }

        try
        {
            // Modifier-only press? (Pressed alone — no other key combined yet.)
            if (TryGetModifierName(input.KeyCode, out var modifierName))
            {
                if (_settings.SpeakModifiers)
                {
                    _speak(modifierName!);
                }
                return;
            }

            // Named navigation / control keys — the user wants to hear "Tab", "Escape", etc.
            var navName = MapNavigationKey(input.KeyCode);
            if (navName is not null)
            {
                HandleWordBreak(input.KeyCode);
                if (_settings.SpeakNavigationKeys)
                {
                    _speak(navName);
                }
                return;
            }

            // Backspace — announce the character it removes.
            //
            // Taken from the word buffer rather than read back from the
            // control. The buffer already holds exactly what the user typed,
            // so this is exact and free, with no race against the application
            // processing the keystroke. Reading the control instead means
            // racing the deletion: the low-level hook fires before the app
            // sees the key, but our handler runs after the input channel, so
            // by the time a read lands the character may already be gone.
            //
            // The buffer is empty when the user arrowed into existing text and
            // started deleting. There is no cheap exact answer there, so we
            // fall back to naming the key. See docs/ROADMAP.md 3.6 #6.
            if (input.KeyCode == VK_BACK)
            {
                _onTypingActivity?.Invoke();
                string? removed = null;
                lock (_gate)
                {
                    if (_wordBuffer.Length > 0)
                    {
                        removed = _wordBuffer[^1].ToString();
                        _wordBuffer.Length--;
                    }
                }
                if (removed is not null && _settings.SpeakCharacters)
                {
                    _speak(removed);
                }
                else if (_settings.SpeakNavigationKeys)
                {
                    _speak("backspace");
                }
                return;
            }

            // Printable character.
            var ch = KeyTranslator.ToCharacter(input.KeyCode, input.Modifiers);
            if (ch is not { } typed)
            {
                return;
            }
            // Mark a typing keystroke so the speech pipeline can mute the
            // value-changed announcement that fires immediately after on
            // edits / comboboxes (the Run dialog "open: notep" repeating
            // value bug).
            _onTypingActivity?.Invoke();

            // A word is finished by whitespace or sentence punctuation — NOT by
            // apostrophes or hyphens. char.IsPunctuation includes both, which
            // is why this used to say "don" then "t". WordBoundary is the one
            // definition, shared with the text model.
            if (WordBoundary.IsTerminator(typed))
            {
                FlushWord();
                if (_settings.SpeakCharacters && !char.IsWhiteSpace(typed))
                {
                    _speak(typed.ToString());
                }
                return;
            }

            lock (_gate)
            {
                _wordBuffer.Append(typed);
            }
            if (_settings.SpeakCharacters)
            {
                _speak(typed.ToString());
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Warning(ex, "key echo threw on vk=0x{Vk:X2}", input.KeyCode);
        }
    }

    private void HandleWordBreak(int keyCode)
    {
        switch (keyCode)
        {
            case VK_SPACE:
            case VK_RETURN:
            case VK_TAB:
                FlushWord();
                break;
            case VK_LEFT:
            case VK_UP:
            case VK_RIGHT:
            case VK_DOWN:
            case VK_HOME:
            case VK_END:
            case VK_PRIOR:
            case VK_NEXT:
                lock (_gate)
                {
                    _wordBuffer.Clear();
                }
                break;
        }
    }

    private void FlushWord()
    {
        string word;
        lock (_gate)
        {
            if (_wordBuffer.Length == 0)
            {
                return;
            }
            word = _wordBuffer.ToString();
            _wordBuffer.Clear();
        }
        if (_settings.SpeakWords)
        {
            _speak(word);
        }
    }

    private static bool TryGetModifierName(int keyCode, out string? name)
    {
        name = keyCode switch
        {
            0x10 or 0xA0 or 0xA1 => "shift",
            0x11 or 0xA2 or 0xA3 => "control",
            0x12 or 0xA4 or 0xA5 => "alt",
            0x5B or 0x5C => "windows",
            _ => null,
        };
        return name is not null;
    }

    private static string? MapNavigationKey(int keyCode) => keyCode switch
    {
        VK_ESCAPE => "escape",
        VK_TAB => "tab",
        VK_RETURN => "enter",
        VK_DELETE => "delete",
        VK_INSERT => "insert",
        VK_HOME => "home",
        VK_END => "end",
        VK_PRIOR => "page up",
        VK_NEXT => "page down",
        VK_LEFT => "left",
        VK_UP => "up",
        VK_RIGHT => "right",
        VK_DOWN => "down",
        >= VK_F1 and <= VK_F12 => $"f{keyCode - VK_F1 + 1}",
        _ => null,
    };
}
