using System.Runtime.Versioning;
using OpenReader.Abstractions.Text;
using OpenReader.Platform.Windows.Accessibility.Native;
using Windows.Win32.UI.Accessibility;
using OurTextUnit = OpenReader.Abstractions.Text.TextUnit;
// global:: qualified — plain 'Windows' would bind to OpenReader.Platform.Windows.
using UiaUnit = global::Windows.Win32.UI.Accessibility.TextUnit;

namespace OpenReader.Platform.Windows.Text;

/// <summary>
/// <see cref="ITextRange"/> over a native <see cref="IUIAutomationTextRange"/>.
/// </summary>
/// <remarks>
/// Every call is wrapped: a range whose control has gone away answers empty or
/// zero rather than throwing. Exceptions from a dying provider are routine on
/// the speech path, and handling them once here is what keeps every caller
/// above free of try/catch.
/// </remarks>
// windows6.1 rather than bare "windows": the native UIA COM surface is
// annotated 6.1+, and an unversioned claim asserts support back to XP.
[SupportedOSPlatform("windows6.1")]
internal sealed class NativeUiaTextRange : ITextRange
{
    private readonly NativeUiaTextSurface _surface;
    private readonly IUIAutomationTextRange _range;

    internal NativeUiaTextRange(NativeUiaTextSurface surface, IUIAutomationTextRange range)
    {
        _surface = surface;
        _range = range;
    }

    internal IUIAutomationTextRange Inner => _range;

    public ITextRange Clone()
    {
        try { return new NativeUiaTextRange(_surface, _range.Clone()); }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { return this; }
    }

    public bool IsCollapsed
    {
        get
        {
            try
            {
                return _range.CompareEndpoints(
                    TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                    _range,
                    TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) == 0;
            }
            catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { return true; }
        }
    }

    public string GetText(int maxLength = -1)
    {
        try { return _range.GetText(maxLength).ToString() ?? string.Empty; }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { return string.Empty; }
    }

    public int Move(OurTextUnit unit, int count)
    {
        try { return _range.Move(ToUia(unit), count); }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { return 0; }
    }

    public int MoveEndpoint(RangeEndpoint endpoint, OurTextUnit unit, int count)
    {
        try { return _range.MoveEndpointByUnit(ToUia(endpoint), ToUia(unit), count); }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { return 0; }
    }

    public void ExpandToUnit(OurTextUnit unit)
    {
        try { _range.ExpandToEnclosingUnit(ToUia(unit)); }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { }
    }

    public void Collapse(bool toStart)
    {
        try
        {
            if (toStart)
            {
                _range.MoveEndpointByRange(
                    TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                    _range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            }
            else
            {
                _range.MoveEndpointByRange(
                    TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                    _range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
            }
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { }
    }

    public void SetEndpoint(RangeEndpoint endpoint, ITextRange target, RangeEndpoint targetEndpoint)
    {
        if (target is not NativeUiaTextRange other || !ReferenceEquals(other._surface, _surface))
        {
            return;
        }
        try { _range.MoveEndpointByRange(ToUia(endpoint), other._range, ToUia(targetEndpoint)); }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { }
    }

    public int CompareEndpoints(RangeEndpoint endpoint, ITextRange other, RangeEndpoint otherEndpoint)
    {
        if (other is not NativeUiaTextRange o || !ReferenceEquals(o._surface, _surface))
        {
            return 0;
        }
        try { return _range.CompareEndpoints(ToUia(endpoint), o._range, ToUia(otherEndpoint)); }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { return 0; }
    }

    /// <summary>
    /// Formatting and semantic attributes.
    /// </summary>
    /// <remarks>
    /// <b>Heading level and link target are here, and they were not reachable
    /// at all through the managed client.</b> <c>UIA_StyleIdAttributeId</c> and
    /// <c>UIA_LinkAttributeId</c> arrived in UIA 3, after
    /// <c>System.Windows.Automation</c> froze — and they are exactly what
    /// Read-mode quick navigation ("next heading", "next link") is built on.
    /// This method existing is the point of the whole native migration.
    /// </remarks>
    public IReadOnlyDictionary<string, object?> GetAttributes()
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Heading level. UIA encodes it as StyleId_Heading1..Heading9 (70001+),
        // which we normalise to a plain 1-9.
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_StyleIdAttributeId) is int styleId)
        {
            var heading = styleId - (int)UIA_STYLE_ID.StyleId_Heading1 + 1;
            if (heading is >= 1 and <= 9)
            {
                result[TextAttributes.HeadingLevel] = heading;
            }
        }

        // Presence alone means "this is a link"; the value is the link element.
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_LinkAttributeId) is not null)
        {
            result[TextAttributes.Link] = true;
        }

        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_CultureAttributeId) is int lcid and > 0)
        {
            try
            {
                result[TextAttributes.Language] = System.Globalization.CultureInfo.GetCultureInfo(lcid).Name;
            }
            catch (System.Globalization.CultureNotFoundException) { }
        }
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_FontWeightAttributeId) is int weight)
        {
            result[TextAttributes.Bold] = weight >= 600;
        }
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_IsItalicAttributeId) is bool italic)
        {
            result[TextAttributes.Italic] = italic;
        }
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_UnderlineStyleAttributeId) is int underline)
        {
            result[TextAttributes.Underline] = underline != 0;
        }
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_FontSizeAttributeId) is double size and > 0)
        {
            result[TextAttributes.FontSize] = size;
        }
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_FontNameAttributeId) is string font && font.Length > 0)
        {
            result[TextAttributes.FontName] = font;
        }
        if (Read(UIA_TEXTATTRIBUTE_ID.UIA_AnnotationTypesAttributeId) is int[] annotations)
        {
            // 60003 = AnnotationType_SpellingError, 60004 = GrammarError.
            if (Array.IndexOf(annotations, 60003) >= 0)
            {
                result[TextAttributes.SpellingError] = true;
            }
            if (Array.IndexOf(annotations, 60004) >= 0)
            {
                result[TextAttributes.GrammarError] = true;
            }
        }

        return result;
    }

    /// <summary>
    /// An attribute's value, or <c>null</c> when unsupported or not uniform
    /// across the range. "Mixed" collapses to absent deliberately — a half-bold
    /// range is not bold.
    /// </summary>
    private object? Read(UIA_TEXTATTRIBUTE_ID attribute)
    {
        try
        {
            var value = _range.GetAttributeValue(attribute);
            // Both the "mixed" and "not supported" sentinels are reference
            // markers rather than values; neither is a usable answer.
            return value is null || value.GetType().FullName == "System.__ComObject" ? null : value;
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex)) { return null; }
    }

    /// <summary>
    /// Map our unit onto UIA's. <see cref="OurTextUnit.Sentence"/> degrades to
    /// line: UIA has no sentence unit at all.
    /// </summary>
    private static UiaUnit ToUia(OurTextUnit unit) => unit switch
    {
        OurTextUnit.Character => UiaUnit.TextUnit_Character,
        OurTextUnit.Word => UiaUnit.TextUnit_Word,
        OurTextUnit.Line => UiaUnit.TextUnit_Line,
        OurTextUnit.Sentence => UiaUnit.TextUnit_Line,
        OurTextUnit.Paragraph => UiaUnit.TextUnit_Paragraph,
        OurTextUnit.Page => UiaUnit.TextUnit_Page,
        OurTextUnit.Document => UiaUnit.TextUnit_Document,
        _ => UiaUnit.TextUnit_Character,
    };

    private static TextPatternRangeEndpoint ToUia(RangeEndpoint endpoint)
        => endpoint == RangeEndpoint.Start
            ? TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start
            : TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
}
