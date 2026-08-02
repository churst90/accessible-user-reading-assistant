using Aura.Abstractions.Accessibility;

namespace Aura.Abstractions.Navigation;

/// <summary>
/// Decides whether a document supports Read mode, and builds the buffer.
/// </summary>
/// <remarks>
/// <para>
/// The seam that keeps Read mode out of the core. A web browser plugin, a PDF
/// reader plugin, and a future native document backend each implement this;
/// core asks "can you flatten this?" and does not care who says yes.
/// </para>
/// <para>
/// The alternative — core knowing about browsers — is how a screen reader ends
/// up with per-browser code paths in its dispatch loop that nobody can safely
/// change. Each such provider ships and versions independently.
/// </para>
/// </remarks>
public interface IReadModeProvider
{
    /// <summary>
    /// Ordering hint when several providers claim the same document; higher
    /// wins. First-party providers use 0, so a plugin can deliberately
    /// override one for a site it handles better.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this provider can build a buffer for <paramref name="node"/>.
    /// Must be cheap: it is asked on every focus change.
    /// </summary>
    bool CanHandle(AccessibleNode node);

    /// <summary>
    /// Build a buffer, or return <c>null</c> if the document turns out not to
    /// be usable after all.
    /// </summary>
    /// <remarks>
    /// Expensive — it walks the document tree. Callers build off the event
    /// dispatch path and must expect this to take a perceptible time on a
    /// large page.
    /// </remarks>
    IReadModeBuffer? Build(AccessibleNode node);
}
