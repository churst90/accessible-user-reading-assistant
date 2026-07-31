using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Text;

namespace OpenReader.Core.Text;

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
                return CaretMotion.None;
            }

            motion = CaretMotionResolver.Resolve(_last, current);
            _last = current.Clone();
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
        }
    }
}
