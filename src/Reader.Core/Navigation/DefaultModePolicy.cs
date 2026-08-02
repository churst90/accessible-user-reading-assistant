using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Navigation;

namespace OpenReader.Core.Navigation;

/// <summary>
/// The built-in mode policy: type in things you type into, read everything
/// else — but only inside a document that supports reading.
/// </summary>
/// <remarks>
/// <para>
/// Matches what NVDA and JAWS users expect, because the expectation is already
/// formed and surprising them costs more than any improvement would gain.
/// </para>
/// <para>
/// Replaceable by design. Per-site and per-app preferences ("always Type on
/// this webmail", "never auto-switch in this editor") belong in a policy that
/// wraps this one, not in extra conditions here — that is how this method
/// stays readable instead of becoming a pile of application names.
/// </para>
/// </remarks>
public sealed class DefaultModePolicy : IModePolicy
{
    private readonly Func<AccessibleNode, bool> _isInReadableDocument;

    /// <param name="isInReadableDocument">
    /// Whether this node sits inside something Read mode can present — a web
    /// page, a PDF. Supplied by the host, which knows which
    /// <c>IReadModeProvider</c>s are loaded. Without it every plain dialog
    /// would flip into Read mode and swallow the user's keystrokes.
    /// </param>
    public DefaultModePolicy(Func<AccessibleNode, bool>? isInReadableDocument = null)
    {
        _isInReadableDocument = isInReadableDocument ?? (_ => false);
    }

    public ReaderMode? ModeFor(AccessibleNode node, ReaderMode current)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Outside a readable document there is nothing to read, so Read mode
        // would only eat keystrokes.
        if (!_isInReadableDocument(node))
        {
            return current == ReaderMode.Read ? ReaderMode.Type : null;
        }

        // Landing somewhere the user types means they are about to type.
        if (IsTypingTarget(node))
        {
            return ReaderMode.Type;
        }

        // Everything else in a document is for reading.
        return ReaderMode.Read;
    }

    /// <summary>
    /// Manual overrides stick on editable controls, which is exactly where
    /// users disagree with the automatic choice — proof-reading a long text
    /// box in Read mode is a real workflow, and being yanked out of it on
    /// every focus event is what makes automatic switching feel hostile.
    /// </summary>
    public bool RespectsManualOverride(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return true;
    }

    /// <summary>Roles where a keystroke is almost certainly meant as input.</summary>
    private static bool IsTypingTarget(AccessibleNode node)
    {
        if ((node.States & AccessibleStates.ReadOnly) != 0)
        {
            // A read-only edit is for reading, whatever its role says.
            return false;
        }
        return node.Role is AccessibleRole.Edit
            or AccessibleRole.PasswordEdit
            or AccessibleRole.ComboBox
            or AccessibleRole.Spinner
            or AccessibleRole.Slider;
    }
}
