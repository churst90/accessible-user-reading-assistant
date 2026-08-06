using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Text;

namespace Aura.Core.Text;

/// <summary>
/// Announces caret movement by sampling where the caret is and comparing it
/// with where it was.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the keystroke-classification tracker. The old design had two
/// independent producers of the same announcement — the UIA
/// <c>TextSelectionChanged</c> handler and a keyboard-hook handler — which had
/// to suppress each other through a 250 ms wall-clock window, on top of a
/// fixed 15 ms delay racing the application, a 400 ms duplicate filter, and a
/// 40 ms post-backspace cache refresh. Four constants tuned by ear, and the
/// announcement depended on which producer won.
/// </para>
/// <para>
/// Here there is one producer. A keystroke and a caret event are both merely
/// <em>triggers to re-sample</em>, and are interchangeable — so nothing needs
/// to suppress anything. Two triggers for the same movement resolve the second
/// time to <see cref="CaretMotionKind.None"/> and say nothing, which is what
/// makes the duplicate filter unnecessary rather than merely tuned.
/// </para>
/// <para>
/// Sampling early is now harmless instead of wrong. If the application has not
/// moved the caret yet, the sample resolves to "no movement" and stays silent;
/// the later trigger produces the announcement. That property is what allows
/// <see cref="SampleUntilChangedAsync"/> to poll adaptively rather than betting
/// on a fixed delay — a fast control answers in one read, a slow one gets the
/// full budget, and neither announces a stale position.
/// </para>
/// <para>
/// Platform-free by construction: it talks to <see cref="ITextSurfaceProvider"/>
/// and nothing else, which is why it lives in Core and is covered by ordinary
/// unit tests.
/// </para>
/// </remarks>
public sealed class CaretTracker
{
    private readonly ITextSurfaceProvider _surfaces;
    private readonly Func<AccessibleNode?> _focusedNode;
    private readonly Action<CaretMotion, AccessibleNode> _announce;
    private readonly Func<bool>? _isTyping;
    private readonly object _gate = new();

    private ITextRange? _last;
    private NodeId _lastNodeId;
    private TextUnit? _pendingUnit;
    private string? _charBefore;
    private string? _charAfter;
    private string? _charAfterNext;
    private bool _hasLast;

    public CaretTracker(
        ITextSurfaceProvider surfaces,
        Func<AccessibleNode?> focusedNode,
        Action<CaretMotion, AccessibleNode> announce,
        Func<bool>? isTyping = null)
    {
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _focusedNode = focusedNode ?? throw new ArgumentNullException(nameof(focusedNode));
        _announce = announce ?? throw new ArgumentNullException(nameof(announce));
        _isTyping = isTyping;
    }

    /// <summary>
    /// The character immediately before the caret as of the last observation.
    /// </summary>
    /// <remarks>
    /// Held rather than read on demand, because the only moment it can be read
    /// correctly has already passed by the time anyone wants it. Backspace has
    /// to say what it removed, and the keyboard hook sees the key before the
    /// application does — but announcing from the hook would mean a
    /// cross-process read inside it, and a hook that blocks is a hook Windows
    /// silently unregisters. So it is captured on the way past, whenever the
    /// caret is sampled for any other reason, and read from memory afterwards.
    /// </remarks>
    public string? CharBefore
    {
        get { lock (_gate) { return _charBefore; } }
    }

    /// <summary>The character immediately after the caret — what Delete removes.</summary>
    public string? CharAfter
    {
        get { lock (_gate) { return _charAfter; } }
    }

    /// <summary>
    /// The character two positions ahead — the one that becomes current when
    /// Delete removes the one under the caret.
    /// </summary>
    /// <remarks>
    /// Backspace and Delete want different answers, and the difference is not
    /// arbitrary. Backspace moves the caret left, so what vanished is behind
    /// you and naming it is the only way to know what you lost. Delete leaves
    /// the caret where it is and pulls the rest of the line back under it, so
    /// what you need is what is there <em>now</em> — naming the character that
    /// has already gone tells you nothing about where you are.
    /// </remarks>
    public string? CharAfterNext
    {
        get { lock (_gate) { return _charAfterNext; } }
    }

    /// <summary>How long to keep re-sampling for a change before giving up.</summary>
    public TimeSpan SettleBudget { get; set; } = TimeSpan.FromMilliseconds(60);

    /// <summary>Gap between polls inside <see cref="SettleBudget"/>.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// Forget the stored position. Call on focus change: offsets from the old
    /// control mean nothing in the new one, and diffing across them would
    /// invent a movement that never happened.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _last = null;
            _hasLast = false;
            _lastNodeId = default;
            _pendingUnit = null;
        }
    }

    /// <summary>
    /// Record the granularity the user just asked for, to be applied to the
    /// next motion actually observed.
    /// </summary>
    /// <remarks>
    /// Held rather than passed because the keystroke and the provider's caret
    /// event are both triggers to re-sample, and either may be the one that
    /// sees the movement. Without this the two would resolve the same motion
    /// differently depending on which won the race — the keystroke path
    /// reporting a character and the event path reporting the whole line.
    /// It is consumed by the first non-empty motion and cleared, so it is a
    /// one-shot intent rather than a mode.
    /// </remarks>
    public void RequestUnit(TextUnit? unit)
    {
        lock (_gate)
        {
            _pendingUnit = unit;
        }
    }

    /// <summary>
    /// Take one sample and announce what changed. Returns the motion, mostly so
    /// callers and tests can see whether anything happened.
    /// </summary>
    public CaretMotion Sample()
    {
        if (_isTyping?.Invoke() == true)
        {
            // The user is typing, so the document itself is changing. Offsets
            // taken either side of an edit describe different documents and
            // their difference is meaningless. Key echo owns this case.
            // Re-baseline so the first move after typing isn't diffed against
            // a pre-edit position.
            Rebaseline();
            return CaretMotion.None;
        }

        var node = _focusedNode();
        if (node is null)
        {
            return CaretMotion.None;
        }

        var surface = _surfaces.GetSurface(node);
        if (surface is null)
        {
            return CaretMotion.None;
        }

        var current = surface.GetSelection() ?? surface.GetCaret();
        if (current is null)
        {
            return CaretMotion.None;
        }

        CaretMotion motion;
        lock (_gate)
        {
            // A different control: store the position but say nothing. The
            // focus announcement already told the user where they are.
            if (!_hasLast || _lastNodeId != node.Id)
            {
                _last = current.Clone();
                _lastNodeId = node.Id;
                _hasLast = true;
                CaptureNeighbours(current);
                return CaretMotion.None;
            }

            motion = CaretMotionResolver.Resolve(_last, current, _pendingUnit);
            _last = current.Clone();
            CaptureNeighbours(current);
            if (motion.Kind != CaretMotionKind.None)
            {
                _pendingUnit = null;
            }
        }

        if (motion.Kind != CaretMotionKind.None)
        {
            _announce(motion, node);
        }
        return motion;
    }

    /// <summary>
    /// Sample repeatedly until something changes or <see cref="SettleBudget"/>
    /// runs out.
    /// </summary>
    /// <remarks>
    /// For controls that raise no caret event of their own — classic Notepad,
    /// legacy Win32 edits — the keystroke is the only trigger, and it arrives
    /// <em>before</em> the application has processed it. Rather than guess a
    /// delay, poll: the first sample that shows movement wins and the loop
    /// stops, so a responsive control costs one read and an overloaded one
    /// still gets answered instead of being announced stale.
    /// </remarks>
    public async Task<CaretMotion> SampleUntilChangedAsync(CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + SettleBudget;
        while (true)
        {
            var motion = Sample();
            if (motion.Kind != CaretMotionKind.None)
            {
                return motion;
            }
            if (DateTimeOffset.UtcNow >= deadline || cancellationToken.IsCancellationRequested)
            {
                return CaretMotion.None;
            }
            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CaretMotion.None;
            }
        }
    }

    /// <summary>Store the current position without announcing anything.</summary>
    private void Rebaseline()
    {
        var node = _focusedNode();
        if (node is null)
        {
            return;
        }
        var current = _surfaces.GetSurface(node)?.GetSelection();
        if (current is null)
        {
            return;
        }
        lock (_gate)
        {
            _last = current.Clone();
            _lastNodeId = node.Id;
            _hasLast = true;
            CaptureNeighbours(current);
        }
    }

    /// <summary>
    /// Read the characters either side of the caret. Caller holds the gate.
    /// </summary>
    /// <remarks>
    /// One clone and two single-character expansions. It rides along with a
    /// sample that was happening anyway, so the cost is bounded by how often
    /// the caret moves rather than by how often anyone asks.
    /// </remarks>
    private void CaptureNeighbours(ITextRange current)
    {
        try
        {
            // Move-then-expand, never move-one-endpoint-past-the-other. The
            // first version of this moved Start forward and End forward by a
            // different amount from a collapsed range, so Start briefly sat
            // after End; implementations normalise that by collapsing, and the
            // range that came back was two or three characters wide. Delete
            // then read several characters at once, which is what Cody heard.
            _charBefore = ReadCharacterAt(current, -1);
            _charAfter = ReadCharacterAt(current, 0);
            _charAfterNext = ReadCharacterAt(current, 1);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _charBefore = null;
            _charAfter = null;
            _charAfterNext = null;
        }
    }

    /// <summary>
    /// One character, <paramref name="offset"/> characters from the caret.
    /// <c>0</c> is the character the caret sits on, <c>-1</c> the one behind it.
    /// </summary>
    private static string? ReadCharacterAt(ITextRange caret, int offset)
    {
        var probe = caret.Clone();
        probe.Collapse(toStart: true);
        if (offset != 0 && probe.Move(TextUnit.Character, offset) == 0)
        {
            return null;
        }
        probe.ExpandToUnit(TextUnit.Character);
        var text = probe.GetText(8);
        return text.Length == 0 ? null : text;
    }
}
