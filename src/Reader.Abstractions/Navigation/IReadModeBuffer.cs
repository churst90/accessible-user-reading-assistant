using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Text;

namespace OpenReader.Abstractions.Navigation;

/// <summary>
/// A document flattened into a linear reading surface, for
/// <see cref="ReaderMode.Read"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an <see cref="ITextSurface"/>, and that is the entire design.</b>
/// Review navigation, say-all, selection reporting, caret following and
/// braille are already written against <see cref="ITextRange"/> and have no
/// idea what is behind it. Making Read mode another backend means every one of
/// them works over a web page on the day this ships, with no changes.
/// </para>
/// <para>
/// This is the single most important lesson to take from NVDA. Its browse mode
/// is a <c>TextInfo</c> implementation, which is why the rest of NVDA kept
/// working when browse mode landed. A screen reader that builds a separate
/// navigation stack for web content ends up maintaining two of everything, and
/// they drift.
/// </para>
/// <para>
/// Everything beyond <see cref="ITextSurface"/> is on this interface: turning
/// a tree into a line of text, jumping between element kinds, and getting back
/// to the live element when the user wants to act on it.
/// </para>
/// </remarks>
public interface IReadModeBuffer : ITextSurface
{
    /// <summary>The node this buffer was built from — the document root.</summary>
    AccessibleNode Root { get; }

    /// <summary>
    /// True when the buffer still reflects the document. A buffer over a page
    /// that has since mutated is stale and must be rebuilt before use.
    /// </summary>
    /// <remarks>
    /// Staleness is the hard problem of Read mode, not the flattening. Modern
    /// pages mutate constantly, and a buffer that silently drifts from the
    /// document announces text that is no longer on screen and activates the
    /// wrong element. Prefer rebuilding too often over trusting a stale
    /// buffer.
    /// </remarks>
    bool IsCurrent { get; }

    /// <summary>
    /// Find the next element of a kind, or <c>null</c> when there is none in
    /// that direction.
    /// </summary>
    /// <param name="from">Where to search from — usually the read cursor.</param>
    /// <param name="target">The kind of element to find.</param>
    /// <param name="backwards">Search towards the start of the document.</param>
    /// <param name="level">
    /// Depth for <see cref="NavigationTarget.HeadingAtLevel"/>; ignored otherwise.
    /// </param>
    /// <remarks>
    /// Returning <c>null</c> rather than wrapping is deliberate. Silently
    /// wrapping to the top loses a blind user completely — they press <c>h</c>
    /// expecting the next heading and get one, with no indication they have
    /// travelled backwards past everything they already read. The caller
    /// announces "no next heading" and stays put.
    /// </remarks>
    ITextRange? FindNext(ITextRange from, NavigationTarget target, bool backwards = false, int level = 0);

    /// <summary>
    /// The live accessibility node behind a position, so the caller can read
    /// its role and state, or act on it.
    /// </summary>
    AccessibleNode? NodeAt(ITextRange position);

    /// <summary>
    /// Invoke whatever is at this position — follow a link, press a button,
    /// toggle a checkbox. Returns false when there is nothing actionable.
    /// </summary>
    bool Activate(ITextRange position);

    /// <summary>
    /// Move the application's real focus to the element at this position,
    /// which is how Read mode hands over to <see cref="ReaderMode.Type"/>.
    /// </summary>
    bool SetFocus(ITextRange position);
}
