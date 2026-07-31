using OpenReader.Abstractions.Accessibility;

namespace OpenReader.Abstractions.Text;

/// <summary>
/// Resolves an <see cref="ITextSurface"/> for a node. Implemented by the
/// platform layer, which picks the best backend available for that particular
/// control (UIA text pattern, Win32 messages, plain value text) and hides the
/// choice from everything above.
/// </summary>
/// <remarks>
/// The fallback chain belongs <em>here</em>, behind one call. Today it is
/// open-coded in three places — <c>CaretLineTracker</c>,
/// <c>UiaTextContentProvider</c>, and <c>ReviewCursor</c> — each with its own
/// slightly different ordering and its own bugs.
/// </remarks>
public interface ITextSurfaceProvider
{
    /// <summary>
    /// The text surface for <paramref name="node"/>, or <c>null</c> when the
    /// node exposes no text at all. Cheap to call repeatedly: implementations
    /// should cache per node and invalidate on focus change.
    /// </summary>
    ITextSurface? GetSurface(AccessibleNode node);
}
