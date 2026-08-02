using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Input;
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
/// The keystroke set carries no meaning. It is not "Left means character" —
/// the application decides that, and <see cref="CaretMotionResolver"/> reads
/// the decision off the resulting position. It is only "this key might have
/// moved the caret, so go and look."
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
    private const string EndOfLineToken = "end of line";

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
        if (!MightMoveCaret(input.KeyCode))
        {
            return;
        }

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
    /// Keys that could plausibly move the caret. Membership only — nothing
    /// here implies anything about <em>what</em> to announce.
    /// </summary>
    /// <remarks>
    /// Backspace and Delete are deliberately absent. They change the document,
    /// and two positions taken either side of an edit describe different
    /// documents, so diffing them would announce nonsense. Saying what was
    /// just deleted needs the text captured <em>before</em> the deletion,
    /// which is a job for key echo, not for position comparison.
    /// </remarks>
    private static bool MightMoveCaret(int vk) => vk switch
    {
        0x09 /* VK_TAB    */ => true,
        0x0D /* VK_RETURN */ => true,
        0x21 /* VK_PRIOR  */ => true,
        0x22 /* VK_NEXT   */ => true,
        0x23 /* VK_END    */ => true,
        0x24 /* VK_HOME   */ => true,
        0x25 /* VK_LEFT   */ => true,
        0x26 /* VK_UP     */ => true,
        0x27 /* VK_RIGHT  */ => true,
        0x28 /* VK_DOWN   */ => true,
        _ => false,
    };

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

        string? text = motion.Text;
        switch (motion.Kind)
        {
            case CaretMotionKind.None:
                return null;

            case CaretMotionKind.Character when string.IsNullOrEmpty(text):
                // The caret sits past the last character of the line.
                text = EndOfLineToken;
                break;

            case CaretMotionKind.Line when string.IsNullOrEmpty(text):
                text = BlankLineToken;
                break;

            case CaretMotionKind.Word when string.IsNullOrEmpty(text):
                // Landed on whitespace between words; nothing worth saying.
                return null;

            case CaretMotionKind.SelectionGrew:
                text = string.IsNullOrEmpty(text) ? null : text + ", selected";
                break;

            case CaretMotionKind.SelectionShrank:
            case CaretMotionKind.SelectionCleared:
                text = string.IsNullOrEmpty(text) ? null : text + ", unselected";
                break;
        }

        return string.IsNullOrEmpty(text)
            ? null
            : new SpeechRequest(SpeechReason.CaretMoved, node, RawText: text, AppExecutableName: null);
    }
}
