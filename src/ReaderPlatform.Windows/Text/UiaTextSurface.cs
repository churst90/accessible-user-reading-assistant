using System.Runtime.Versioning;
using System.Windows.Automation;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Text;
using OurTextUnit = OpenReader.Abstractions.Text.TextUnit;

namespace OpenReader.Platform.Windows.Text;

/// <summary>
/// <see cref="ITextSurface"/> over a UIA <c>TextPattern</c>. The primary text
/// backend on Windows — modern WPF, WinUI, Chromium, Office and Electron
/// controls all expose text this way.
/// </summary>
/// <remarks>
/// <para>
/// The pattern is re-acquired on every call rather than cached. UIA text
/// ranges are provider-owned objects with no lifetime guarantee across events;
/// several providers invalidate them as soon as the control's content changes,
/// and a stale range answers with stale text instead of failing loudly. One
/// extra call per sample is a cheap price for never reading a phantom
/// position.
/// </para>
/// <para>
/// <b>Known gap:</b> these calls are cross-process and the managed UIA client
/// exposes no timeout, so a hung provider blocks the caller. Callers on the
/// speech path must therefore not run them on the single dispatch loop. See
/// <c>ASSESSMENT.md</c> S1; native <c>IUIAutomation</c> supports per-thread
/// timeouts and is the real fix.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class UiaTextSurface : ITextSurface
{
    private readonly AutomationElement _element;

    internal UiaTextSurface(AutomationElement element, NodeId nodeId)
    {
        _element = element;
        NodeId = nodeId;
    }

    public NodeId NodeId { get; }

    /// <summary>
    /// UIA defines all seven units, but a provider may quietly degrade one it
    /// does not implement (most Win32 proxies treat Sentence as Line). Callers
    /// that need certainty should compare what they asked for against what
    /// <see cref="ITextRange.Move"/> reports it moved.
    /// </summary>
    public bool SupportsUnit(OurTextUnit unit) => true;

    /// <summary>
    /// The insertion point, collapsed.
    /// </summary>
    /// <remarks>
    /// When a selection is active, UIA does not say which end is the active
    /// one, so this collapses to the end — the correct guess for a
    /// left-to-right forward selection, which is the overwhelmingly common
    /// case. Callers that care about selection should use
    /// <see cref="GetSelection"/>, which is unambiguous.
    /// </remarks>
    public ITextRange? GetCaret()
    {
        var selection = GetSelection();
        selection?.Collapse(toStart: false);
        return selection;
    }

    public ITextRange? GetSelection()
    {
        var pattern = TryGetPattern();
        if (pattern is null)
        {
            return null;
        }
        try
        {
            var selection = pattern.GetSelection();
            if (selection is not { Length: > 0 } || selection[0] is null)
            {
                return null;
            }
            return new UiaTextRange(this, selection[0]);
        }
        catch (Exception ex) when (UiaTextRange.IsProviderFailure(ex))
        {
            return null;
        }
    }

    public ITextRange GetDocumentRange()
    {
        var pattern = TryGetPattern();
        if (pattern is not null)
        {
            try
            {
                var range = pattern.DocumentRange;
                if (range is not null)
                {
                    return new UiaTextRange(this, range);
                }
            }
            catch (Exception ex) when (UiaTextRange.IsProviderFailure(ex))
            {
            }
        }
        return EmptyTextRange.Instance;
    }

    private TextPattern? TryGetPattern()
    {
        try
        {
            return _element.TryGetCurrentPattern(TextPattern.Pattern, out var raw) && raw is TextPattern p
                ? p
                : null;
        }
        catch (Exception ex) when (UiaTextRange.IsProviderFailure(ex))
        {
            return null;
        }
    }
}
