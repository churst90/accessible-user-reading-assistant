namespace OpenReader.Abstractions.Text;

/// <summary>Which end of an <see cref="ITextRange"/> an operation applies to.</summary>
public enum RangeEndpoint
{
    /// <summary>The earlier endpoint in document order.</summary>
    Start,

    /// <summary>The later endpoint in document order.</summary>
    End,
}
