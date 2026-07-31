using OpenReader.Abstractions.Accessibility;

namespace OpenReader.Abstractions.Text;

/// <summary>
/// A text-bearing object the reader can navigate: an edit control, a document,
/// a terminal buffer, or (later) a browse-mode virtual buffer over a web page.
/// </summary>
/// <remarks>
/// <para>
/// This is the third seam between platform and core, alongside
/// <see cref="IAccessibilityProvider"/> and
/// <see cref="ITextContentProvider"/>. It exists so that every text behaviour
/// is written <em>once</em>, against <see cref="ITextRange"/>, and works
/// unchanged over each backend:
/// </para>
/// <list type="bullet">
///   <item>UIA <c>TextPattern</c> — the primary Windows backend.</item>
///   <item>Win32 <c>EM_*</c> messages — the fallback for classic edits that
///         expose no text pattern.</item>
///   <item>A plain string — the synthetic backend used by tests, and the
///         adapter that turns any "whole text + caret offset" source into a
///         real surface.</item>
///   <item>A virtual buffer — browse mode, later.</item>
/// </list>
/// <para>
/// The payoff is that caret following, review navigation, say-all, selection
/// reporting and braille all stop caring which backend they are talking to.
/// </para>
/// </remarks>
public interface ITextSurface
{
    /// <summary>The node this surface belongs to.</summary>
    NodeId NodeId { get; }

    /// <summary>
    /// True when this backend can move by <paramref name="unit"/> natively.
    /// A caller may still ask for an unsupported unit; the range degrades to
    /// the nearest supported one.
    /// </summary>
    bool SupportsUnit(TextUnit unit);

    /// <summary>
    /// A collapsed range at the insertion point, or <c>null</c> when the
    /// surface has no caret (a read-only label, a control that lost focus).
    /// </summary>
    ITextRange? GetCaret();

    /// <summary>
    /// The current selection, or <c>null</c> when nothing is selected. A
    /// collapsed result means "caret only" and is equivalent to
    /// <see cref="GetCaret"/>.
    /// </summary>
    ITextRange? GetSelection();

    /// <summary>A range spanning the whole surface. The anchor for say-all and review.</summary>
    ITextRange GetDocumentRange();
}
