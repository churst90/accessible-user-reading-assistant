using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Navigation;

namespace Aura.Core.Navigation;

/// <summary>
/// Owns the current <see cref="ReaderMode"/> and decides when it changes.
/// </summary>
/// <remarks>
/// <para>
/// Automatic mode switching is what makes Read mode usable and also what makes
/// users furious. Land in a search box and the reader must already be in
/// <see cref="ReaderMode.Write"/>; land in a rich-text editor to proof-read and
/// it must not be. There is no universally correct answer, so the decision
/// lives behind <see cref="IModePolicy"/> and this class only enforces the
/// rules around it.
/// </para>
/// <para>
/// The rule that matters most is the manual override. A user who deliberately
/// switches mode must not be thrown back by the next focus event — that reads
/// as the reader fighting them, and it is the single most common complaint
/// about every implementation of this feature. The override is remembered per
/// control and cleared when focus moves somewhere the policy has an opinion
/// about.
/// </para>
/// </remarks>
public sealed class ModeManager
{
    private readonly IModePolicy _policy;
    private readonly object _gate = new();
    private ReaderMode _mode = ReaderMode.Write;
    private NodeId _overriddenFor;
    private bool _hasOverride;

    public ModeManager(IModePolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>The mode right now.</summary>
    public ReaderMode Mode
    {
        get { lock (_gate) { return _mode; } }
    }

    /// <summary>
    /// Raised when the mode actually changes, with the new mode and whether
    /// the user asked for it. The host announces "read mode" / "write mode";
    /// silent switching leaves the user unable to explain why their keystrokes
    /// stopped working.
    /// </summary>
    public event Action<ReaderMode, bool>? ModeChanged;

    /// <summary>
    /// Toggle the mode because the user pressed the toggle key. Sticks against
    /// the policy for as long as focus stays on this control.
    /// </summary>
    public ReaderMode Toggle(AccessibleNode? focused)
    {
        ReaderMode next;
        lock (_gate)
        {
            next = _mode == ReaderMode.Read ? ReaderMode.Write : ReaderMode.Read;
            _mode = next;
            if (focused is not null)
            {
                _overriddenFor = focused.Id;
                _hasOverride = true;
            }
        }
        ModeChanged?.Invoke(next, true);
        return next;
    }

    /// <summary>
    /// Re-evaluate the mode for a new focus target. Returns the mode in effect
    /// afterwards.
    /// </summary>
    public ReaderMode OnFocusChanged(AccessibleNode? focused)
    {
        if (focused is null)
        {
            return Mode;
        }

        ReaderMode result;
        bool changed;
        lock (_gate)
        {
            // A manual override survives only on the control it was made for.
            if (_hasOverride && _overriddenFor == focused.Id && _policy.RespectsManualOverride(focused))
            {
                return _mode;
            }
            _hasOverride = false;

            var wanted = _policy.ModeFor(focused, _mode);
            if (wanted is null || wanted == _mode)
            {
                // A policy that answers every question fights the user. Most
                // focus changes should leave the mode alone.
                return _mode;
            }
            _mode = wanted.Value;
            result = _mode;
            changed = true;
        }

        if (changed)
        {
            ModeChanged?.Invoke(result, false);
        }
        return result;
    }

    /// <summary>Force a mode without recording a manual override. For tests and profile loads.</summary>
    public void Set(ReaderMode mode)
    {
        bool changed;
        lock (_gate)
        {
            changed = _mode != mode;
            _mode = mode;
            _hasOverride = false;
        }
        if (changed)
        {
            ModeChanged?.Invoke(mode, false);
        }
    }
}
