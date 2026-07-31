using System.Runtime.Versioning;
using System.Windows.Automation;
using OpenReader.Abstractions.Text;
using UiaTextUnit = System.Windows.Automation.Text.TextUnit;
using UiaEndpoint = System.Windows.Automation.Text.TextPatternRangeEndpoint;
// Aliased rather than importing System.Windows.Automation.Text wholesale: that
// namespace also defines a TextUnit, which would collide with ours.
using UiaRange = System.Windows.Automation.Text.TextPatternRange;
using OurTextUnit = OpenReader.Abstractions.Text.TextUnit;

namespace OpenReader.Platform.Windows.Text;

/// <summary>
/// <see cref="ITextRange"/> over a UIA <see cref="UiaRange"/>.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is deliberately one-to-one — our contract was modelled on
/// <c>ITextRangeProvider</c> precisely so this adapter could stay thin and so
/// <c>StringTextSurface</c> could remain a faithful reference implementation
/// of the same semantics.
/// </para>
/// <para>
/// <b>Every call is wrapped.</b> A range whose control has gone away answers
/// empty or zero rather than throwing. That is the contract, and it is what
/// lets the fourteen scattered try/catch blocks above this layer disappear:
/// exceptions from a dying provider are a routine condition on the speech
/// path, not an exceptional one, and handling them once at the boundary is
/// the only way to keep the callers readable.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class UiaTextRange : ITextRange
{
    private readonly UiaTextSurface _surface;
    private readonly UiaRange _range;

    internal UiaTextRange(UiaTextSurface surface, UiaRange range)
    {
        _surface = surface;
        _range = range;
    }

    internal UiaRange Inner => _range;

    internal UiaTextSurface Surface => _surface;

    public ITextRange Clone()
    {
        try
        {
            return new UiaTextRange(_surface, _range.Clone());
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return this;
        }
    }

    public bool IsCollapsed
    {
        get
        {
            try
            {
                return _range.CompareEndpoints(UiaEndpoint.Start, _range, UiaEndpoint.End) == 0;
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                return true;
            }
        }
    }

    public string GetText(int maxLength = -1)
    {
        try
        {
            return _range.GetText(maxLength) ?? string.Empty;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return string.Empty;
        }
    }

    public int Move(OurTextUnit unit, int count)
    {
        try
        {
            return _range.Move(ToUia(unit), count);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return 0;
        }
    }

    public int MoveEndpoint(RangeEndpoint endpoint, OurTextUnit unit, int count)
    {
        try
        {
            return _range.MoveEndpointByUnit(ToUia(endpoint), ToUia(unit), count);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return 0;
        }
    }

    public void ExpandToUnit(OurTextUnit unit)
    {
        try
        {
            _range.ExpandToEnclosingUnit(ToUia(unit));
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
        }
    }

    public void Collapse(bool toStart)
    {
        try
        {
            // Drag the far endpoint onto the near one.
            if (toStart)
            {
                _range.MoveEndpointByRange(UiaEndpoint.End, _range, UiaEndpoint.Start);
            }
            else
            {
                _range.MoveEndpointByRange(UiaEndpoint.Start, _range, UiaEndpoint.End);
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
        }
    }

    public void SetEndpoint(RangeEndpoint endpoint, ITextRange target, RangeEndpoint targetEndpoint)
    {
        if (target is not UiaTextRange other || !ReferenceEquals(other._surface, _surface))
        {
            return;
        }
        try
        {
            _range.MoveEndpointByRange(ToUia(endpoint), other._range, ToUia(targetEndpoint));
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
        }
    }

    public int CompareEndpoints(RangeEndpoint endpoint, ITextRange other, RangeEndpoint otherEndpoint)
    {
        if (other is not UiaTextRange o || !ReferenceEquals(o._surface, _surface))
        {
            return 0;
        }
        try
        {
            return _range.CompareEndpoints(ToUia(endpoint), o._range, ToUia(otherEndpoint));
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return 0;
        }
    }

    /// <summary>
    /// Formatting attributes, limited to what the managed UIA client exposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Heading level and link target are missing here, and that is not an
    /// oversight.</b> <c>UIA_StyleIdAttributeId</c> (40034) and
    /// <c>UIA_LinkAttributeId</c> (40035) were added in UIA 3;
    /// <c>System.Windows.Automation</c> froze before them and exposes no
    /// <c>AutomationTextAttribute</c> for either.
    /// </para>
    /// <para>
    /// Those two attributes are exactly what browse-mode quick navigation is
    /// built on — "next heading", "next link". This method is therefore the
    /// concrete, checkable form of the argument in <c>ASSESSMENT.md</c> S2:
    /// Phase 4c is not merely slower on the managed client, parts of it are
    /// not expressible. Migrating to native <c>IUIAutomation</c> fills this in
    /// without any change above this line.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, object?> GetAttributes()
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (Read(TextPattern.CultureAttribute) is int lcid and > 0)
        {
            try
            {
                result[TextAttributes.Language] = System.Globalization.CultureInfo
                    .GetCultureInfo(lcid).Name;
            }
            catch (System.Globalization.CultureNotFoundException)
            {
            }
        }

        // UIA reports weight numerically; 600 is the conventional bold threshold.
        if (Read(TextPattern.FontWeightAttribute) is int weight)
        {
            result[TextAttributes.Bold] = weight >= 600;
        }
        if (Read(TextPattern.IsItalicAttribute) is bool italic)
        {
            result[TextAttributes.Italic] = italic;
        }
        if (Read(TextPattern.UnderlineStyleAttribute) is int underline)
        {
            result[TextAttributes.Underline] = underline != 0;
        }
        if (Read(TextPattern.FontSizeAttribute) is double size and > 0)
        {
            result[TextAttributes.FontSize] = size;
        }
        if (Read(TextPattern.FontNameAttribute) is string font && font.Length > 0)
        {
            result[TextAttributes.FontName] = font;
        }

        return result;
    }

    /// <summary>
    /// An attribute's value, or <c>null</c> when it is unsupported or not
    /// uniform across the range. "Mixed" collapses to absent deliberately: the
    /// contract says a value is present only when it holds for the whole
    /// range, and a half-bold range is not bold.
    /// </summary>
    private object? Read(AutomationTextAttribute attribute)
    {
        try
        {
            var value = _range.GetAttributeValue(attribute);
            if (ReferenceEquals(value, TextPattern.MixedAttributeValue)
                || ReferenceEquals(value, AutomationElement.NotSupported))
            {
                return null;
            }
            return value;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Map our unit onto UIA's. <see cref="OurTextUnit.Sentence"/> degrades to
    /// <c>Line</c> — UIA has no sentence unit at all (its enum runs Character,
    /// Format, Word, Line, Paragraph, Page, Document), which is the same
    /// degradation <c>StringTextSurface</c> makes.
    /// </summary>
    private static UiaTextUnit ToUia(OurTextUnit unit) => unit switch
    {
        OurTextUnit.Character => UiaTextUnit.Character,
        OurTextUnit.Word => UiaTextUnit.Word,
        OurTextUnit.Line => UiaTextUnit.Line,
        OurTextUnit.Sentence => UiaTextUnit.Line,
        OurTextUnit.Paragraph => UiaTextUnit.Paragraph,
        OurTextUnit.Page => UiaTextUnit.Page,
        OurTextUnit.Document => UiaTextUnit.Document,
        _ => UiaTextUnit.Character,
    };

    private static UiaEndpoint ToUia(RangeEndpoint endpoint)
        => endpoint == RangeEndpoint.Start ? UiaEndpoint.Start : UiaEndpoint.End;

    /// <summary>
    /// Exceptions that mean "the provider can no longer answer", as opposed to
    /// a defect on our side. <see cref="OutOfMemoryException"/> and friends are
    /// deliberately not included — those must keep propagating.
    /// </summary>
    internal static bool IsProviderFailure(Exception ex)
        => ex is ElementNotAvailableException
            or InvalidOperationException
            or ArgumentException
            or System.Runtime.InteropServices.COMException
            or NotSupportedException;
}
