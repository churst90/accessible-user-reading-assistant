
namespace Aura.Abstractions.Text;

/// <summary>
/// A range over nothing. Returned where the contract promises a range but no
/// backend could produce one.
/// </summary>
/// <remarks>
/// The contract says <c>GetDocumentRange</c> returns a range, not a nullable
/// one, so a backend that cannot answer needs something to hand back. Making
/// that an object which reads as empty — rather than <c>null</c>, or an
/// exception — is what keeps callers on the speech path free of null checks
/// they would otherwise have to repeat at every use.
/// </remarks>
public sealed class EmptyTextRange : ITextRange
{
    /// <summary>Shared instance; the type carries no state.</summary>
    public static readonly EmptyTextRange Instance = new();

    private static readonly IReadOnlyDictionary<string, object?> NoAttributes
        = new Dictionary<string, object?>(0);

    private EmptyTextRange()
    {
    }

    public bool IsCollapsed => true;

    public ITextRange Clone() => this;

    public string GetText(int maxLength = -1) => string.Empty;

    public int Move(TextUnit unit, int count) => 0;

    public int MoveEndpoint(RangeEndpoint endpoint, TextUnit unit, int count) => 0;

    public void ExpandToUnit(TextUnit unit)
    {
    }

    public void Collapse(bool toStart)
    {
    }

    public void SetEndpoint(RangeEndpoint endpoint, ITextRange target, RangeEndpoint targetEndpoint)
    {
    }

    public int CompareEndpoints(RangeEndpoint endpoint, ITextRange other, RangeEndpoint otherEndpoint)
        => other is EmptyTextRange ? 0 : -1;

    public IReadOnlyDictionary<string, object?> GetAttributes() => NoAttributes;
}
