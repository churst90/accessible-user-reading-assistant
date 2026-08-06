using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Input;
using Aura.Abstractions.Output;
using Aura.Abstractions.Text;
using Aura.Abstractions.Speech;
using Aura.Diagnostics;
using Serilog;

namespace Aura.Core.Text;

/// <summary>
/// Feeds the two "the caret may have moved" signals into a single
/// <see cref="CaretTracker"/> and turns the resulting motion into speech.
/// </summary>
/// <remarks>
/// <para>
/// Replaces <c>CaretLineTracker</c>. That class classified the keystroke to
/// decide what to announce, waited a fixed 15 ms for the application to react,
/// and reached into the UIA provider to silence it for 250 ms so the two
/// announcement paths would not both speak. All of that is gone: the tracker
/// compares observed positions, so the signals below are interchangeable
/// triggers to re-sample and neither has to suppress the other.
/// </para>
/// <para>
/// The keystroke does two things, and an earlier version of this class was
/// wrong to think it did neither. It says <em>go and look</em>, and it says
/// <em>at what granularity</em>. The application still decides where the caret
/// ends up — that is what the position comparison is for — but only the
/// keystroke knows that the user asked for one character rather than a line.
/// Inferring the unit from the distance covered reports a whole line when Left
/// wraps to the previous one, which is a paragraph of speech in answer to a
/// request for one character.
/// </para>
/// <para>
/// Depends on nothing platform-specific: <see cref="IAccessibilityProvider"/>,
/// <see cref="IInputSource"/> and virtual key codes are all above the platform
/// seam. That is why it sits in Core, where it can be tested, rather than
/// inside the Windows assembly where its predecessor lived.
/// </para>
/// </remarks>
public sealed class CaretFollowService : IDisposable
{
    private const string BlankLineToken = "blank";

    /// <summary>
    /// What is said when the caret lands past the last character of a line.
    /// </summary>
    /// <remarks>
    /// "line feed" rather than "end of line": it names the thing that is
    /// actually there, it is shorter to hear at reading speed, and it is what
    /// Cody asked for after listening to the alternative.
    /// </remarks>
    private const string LineEndToken = "line feed";

    private readonly IInputSource _keyboard;
    private readonly IAccessibilityProvider _provider;
    private readonly CaretTracker _tracker;
    private readonly ILogger _log;
    private IDisposable? _caretSubscription;
    private IDisposable? _focusSubscription;
    private bool _started;
    private bool _disposed;

    public CaretFollowService(
        IInputSource keyboard,
        IAccessibilityProvider provider,
        CaretTracker tracker)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _log = LoggerFactory.ForComponent("Core.CaretFollow");
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;

        _keyboard.RawInputReceived += OnRawInput;

        // Controls that raise a real caret event need no keystroke at all —
        // and this path also catches mouse clicks, find results and
        // autocomplete, which no keystroke classifier could ever see.
        _caretSubscription = _provider.Subscribe(
            AccessibilityEventKind.CaretMoved,
            _ => SafeSample());

        // Offsets in the previous control mean nothing in the next one.
        _focusSubscription = _provider.Subscribe(
            AccessibilityEventKind.FocusChanged,
            _ => _tracker.Reset());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _keyboard.RawInputReceived -= OnRawInput;
        _caretSubscription?.Dispose();
        _focusSubscription?.Dispose();
        _caretSubscription = null;
        _focusSubscription = null;
    }

    private void OnRawInput(object? sender, RawInput input)
    {
        if (input.Kind != InputEventKind.KeyDown)
        {
            return;
        }
        // Reader-modified keys drive review-cursor commands, not the caret.
        if ((input.Modifiers & InputModifiers.Reader) != 0)
        {
            return;
        }
        // Alt and Win turn caret keys into application shortcuts
        // (Alt+Left is "back", Win+Left snaps a window).
        if ((input.Modifiers & (InputModifiers.Alt | InputModifiers.Win)) != 0)
        {
            return;
        }
        var requested = RequestedUnit(input);
        if (requested is null)
        {
            return;
        }
        _tracker.RequestUnit(requested);

        // The hook runs before the application has seen the key, so poll for
        // the change rather than guessing how long it will take. A sample that
        // lands too early resolves to "nothing moved" and stays silent.
        _ = Task.Run(async () =>
        {
            try
            {
                await _tracker.SampleUntilChangedAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Verbose(ex, "caret sample after keystroke failed");
            }
        });
    }

    private void SafeSample()
    {
        try
        {
            _tracker.Sample();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Verbose(ex, "caret sample after provider event failed");
        }
    }

    /// <summary>
    /// The granularity a caret key asks for, or <c>null</c> for a key that
    /// cannot move the caret.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backspace and Delete are deliberately absent. They change the document,
    /// and two positions taken either side of an edit describe different
    /// documents, so diffing them would announce nonsense. Saying what was just
    /// deleted needs the text captured <em>before</em> the deletion, which is a
    /// job for key echo.
    /// </para>
    /// <para>
    /// Home and End report a character, matching NVDA: the useful answer to
    /// "End" is what is at the end, which past the last character is the line
    /// ending itself.
    /// </para>
    /// </remarks>
    public static TextUnit? RequestedUnit(RawInput input)
    {
        var byWord = (input.Modifiers & InputModifiers.Control) != 0;
        return input.KeyCode switch
        {
            0x25 /* VK_LEFT  */ => byWord ? TextUnit.Word : TextUnit.Character,
            0x27 /* VK_RIGHT */ => byWord ? TextUnit.Word : TextUnit.Character,
            0x23 /* VK_END   */ => byWord ? TextUnit.Line : TextUnit.Character,
            0x24 /* VK_HOME  */ => byWord ? TextUnit.Line : TextUnit.Character,

            0x26 /* VK_UP     */ => TextUnit.Line,
            0x28 /* VK_DOWN   */ => TextUnit.Line,
            0x21 /* VK_PRIOR  */ => TextUnit.Line,
            0x22 /* VK_NEXT   */ => TextUnit.Line,
            0x09 /* VK_TAB    */ => TextUnit.Line,
            0x0D /* VK_RETURN */ => TextUnit.Line,
            _ => null,
        };
    }

    /// <summary>
    /// Turn a resolved motion into a speech request, or <c>null</c> for
    /// nothing worth saying.
    /// </summary>
    /// <remarks>
    /// The one place that decides how an empty reading is voiced. The tracker
    /// reports empty text rather than inventing a word, because "blank" versus
    /// "end of line" versus silence is a presentation choice and belongs here.
    /// </remarks>
    public static SpeechRequest? ToRequest(CaretMotion motion, AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(motion);

        // Blank.Is, not IsNullOrEmpty. A provider asked to expand an empty line
        // hands back the line terminator itself — "\r\n" — which is not empty
        // and is not audible either, so the reader fell silent on exactly the
        // lines it most needed to announce. This was the "blank lines don't
        // read" symptom, and no amount of work further down the pipeline could
        // have fixed it: by then the announcement was a string of characters
        // that happened to make no sound.
        var text = Trim(motion.Text);
        switch (motion.Kind)
        {
            case CaretMotionKind.None:
                return null;

            case CaretMotionKind.Character when NothingThere(text):
                // Past the last character of the line, or standing on the line
                // ending itself.
                text = LineEndToken;
                break;

            case CaretMotionKind.Line when Blank.Is(text):
                text = BlankLineToken;
                break;

            case CaretMotionKind.Word when Blank.Is(text):
                // Landed on whitespace between words; nothing worth saying.
                return null;

            case CaretMotionKind.SelectionGrew:
                text = Blank.Is(text) ? null : text + ", selected";
                break;

            case CaretMotionKind.SelectionShrank:
            case CaretMotionKind.SelectionCleared:
                text = Blank.Is(text) ? null : text + ", unselected";
                break;
        }

        return string.IsNullOrEmpty(text)
            ? null
            : new SpeechRequest(SpeechReason.CaretMoved, node, RawText: text, AppExecutableName: null);
    }

    /// <summary>
    /// True when there is no character at the caret at all.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> <see cref="Blank.Is"/>, which counts a space as
    /// blank — correct for a whole line, wrong for one character. Reviewing a
    /// sentence with the arrow keys walks onto the spaces between words, and
    /// calling each of them "line feed" is both wrong and confusing. A space is
    /// a real character with a name; only the absence of one, or the line
    /// ending itself, is a line feed. The naming of "space", "tab" and the
    /// punctuation happens downstream in <c>PunctuationFilter.SpokenName</c>,
    /// which is why this only has to let them through.
    /// </remarks>
    private static bool NothingThere(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }
        foreach (var c in text)
        {
            if (c is not ('\r' or '\n' or '\0'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Drop the line terminator a provider includes when it expands to a line.
    /// Speaking it is silence; leaving it in makes a blank line indistinguishable
    /// from a failure to read.
    /// </summary>
    private static string? Trim(string? text)
        => text is null ? null : text.TrimEnd('\r', '\n');
}
