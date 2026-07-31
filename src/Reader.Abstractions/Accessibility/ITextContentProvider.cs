namespace OpenReader.Abstractions.Accessibility;

/// <summary>
/// Reads the text content of a node beyond what
/// <see cref="AccessibleNode.Value"/> exposes (e.g., document bodies that only
/// surface text through UIA <c>TextPattern</c>).
/// </summary>
/// <remarks>
/// This is the second seam between platform and core, alongside
/// <see cref="IAccessibilityProvider"/>. Implementations hold whatever
/// platform-specific handle they need to fetch live text on demand.
/// </remarks>
public interface ITextContentProvider
{
    /// <summary>Return the text content of <paramref name="node"/> or <c>null</c> if none is available.</summary>
    string? GetText(AccessibleNode node);
}
