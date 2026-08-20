using System.Runtime.Versioning;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Text;
using Aura.Diagnostics;
using Aura.Platform.Windows.Accessibility.Native;
using Aura.Platform.Windows.Interop;
using Serilog;
using Windows.Win32.UI.Accessibility;
using OurTextUnit = Aura.Abstractions.Text.TextUnit;

namespace Aura.Platform.Windows.Text;

/// <summary>
/// <see cref="ITextSurface"/> over a native UIA <c>TextPattern</c>.
/// </summary>
/// <remarks>
/// The pattern is re-acquired on every call rather than cached. UIA text ranges
/// are provider-owned with no lifetime guarantee across events, and several
/// providers invalidate them when content changes — a stale range answers with
/// stale text instead of failing loudly. One extra call per sample is cheap
/// next to reading a phantom position.
/// </remarks>
// windows6.1 rather than bare "windows": the native UIA COM surface is
// annotated 6.1+, and an unversioned claim asserts support back to XP.
[SupportedOSPlatform("windows6.1")]
internal sealed class NativeUiaTextSurface : ITextSurface, IDisposable
{
    private readonly ComOwned<IUIAutomationElement> _owned;

    /// <summary>
    /// Takes ownership of <paramref name="element"/>, which must be a reference
    /// held for this surface alone.
    /// </summary>
    /// <remarks>
    /// The surface outlives the event that produced the element — it is cached
    /// and reused while the caret stays in one control — so it cannot borrow the
    /// provider's reference. The provider releases that one as soon as focus
    /// moves, and every call through here afterwards would be a call on an
    /// element already handed back.
    /// </remarks>
    internal NativeUiaTextSurface(ComOwned<IUIAutomationElement> element, NodeId nodeId)
    {
        _owned = element;
        NodeId = nodeId;
    }

    private IUIAutomationElement _element => _owned.Value;

    /// <summary>Release the element. The surface is unusable afterwards.</summary>
    public void Dispose() => _owned.Dispose();

    public NodeId NodeId { get; }

    /// <summary>
    /// UIA defines all the units, but a provider may quietly degrade one it
    /// does not implement. Callers needing certainty should compare what they
    /// asked to move against what <see cref="ITextRange.Move"/> reports.
    /// </summary>
    public bool SupportsUnit(OurTextUnit unit) => true;

    /// <summary>
    /// The insertion point, collapsed. When a selection is active UIA does not
    /// say which end is live, so this collapses to the end — correct for a
    /// forward selection, which is the overwhelmingly common case.
    /// </summary>
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
            var ranges = pattern.GetSelection();
            if (ranges is null || ranges.Length == 0)
            {
                return null;
            }
            return new NativeUiaTextRange(this, ranges.GetElement(0));
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
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
                return new NativeUiaTextRange(this, pattern.DocumentRange);
            }
            catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
            {
            }
        }
        return EmptyTextRange.Instance;
    }

    private IUIAutomationTextPattern? TryGetPattern()
    {
        try
        {
            return _element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId) as IUIAutomationTextPattern;
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            return null;
        }
    }
}

/// <summary>
/// Picks the best text backend for a node and hides the choice.
/// </summary>
/// <remarks>
/// <para>
/// The fallback chain lives here, behind one call: UIA text pattern, then
/// Win32 messages, then the node's own value read-only. It used to be
/// open-coded in three places with three different orderings, and therefore
/// three different sets of controls it silently failed on.
/// </para>
/// <para>
/// <b>Surfaces are cached per node, and that is load-bearing.</b> Ranges only
/// compare against ranges from the same surface instance, and caret following
/// compares a previous position against a current one — handing out a fresh
/// surface per call would make every comparison return "equal" and caret
/// following would silently stop announcing.
/// </para>
/// </remarks>
// windows6.1 rather than bare "windows": the native UIA COM surface is
// annotated 6.1+, and an unversioned claim asserts support back to XP.
[SupportedOSPlatform("windows6.1")]
public sealed class NativeUiaTextSurfaceProvider : ITextSurfaceProvider
{
    private readonly NativeUiaProvider _provider;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private NodeId _cachedId;
    private ITextSurface? _cached;
    private bool _hasCached;

    public NativeUiaTextSurfaceProvider(NativeUiaProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _log = LoggerFactory.ForComponent("UIA.Native.Text");
    }

    public ITextSurface? GetSurface(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        lock (_gate)
        {
            if (_hasCached && _cachedId == node.Id)
            {
                return _cached;
            }
        }

        var surface = Resolve(node);

        ITextSurface? evicted;
        lock (_gate)
        {
            // Only one node is navigated at a time, so one slot is the whole
            // cache; focus moving elsewhere evicts by overwriting.
            evicted = ReferenceEquals(_cached, surface) ? null : _cached;
            _cachedId = node.Id;
            _cached = surface;
            _hasCached = true;
        }
        Release(evicted);
        return surface;
    }

    /// <summary>Drop the cached surface. Call when the focused control may have changed.</summary>
    public void Invalidate()
    {
        ITextSurface? evicted;
        lock (_gate)
        {
            evicted = _cached;
            _hasCached = false;
            _cached = null;
        }
        Release(evicted);
    }

    /// <summary>
    /// Hand back whatever the evicted surface was holding.
    /// </summary>
    /// <remarks>
    /// Only the UIA surfaces own a COM reference; the Win32 and string ones own
    /// nothing and are not disposable. Testing the interface rather than the
    /// concrete type keeps that from being a fact this method has to know.
    /// </remarks>
    private static void Release(ITextSurface? surface)
    {
        if (surface is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private ITextSurface? Resolve(AccessibleNode node)
    {
        var element = _provider.TryGetElement(node.Id);

        // 1. UIA TextPattern. Availability came free in the cached batch.
        if (element is not null && Extra(node, "uia.HasTextPattern") is true)
        {
            _log.Verbose("text surface for {NodeId}: native UIA TextPattern", node.Id);
            // A reference of its own: the element above belongs to the
            // provider's focus cache and is released the moment focus moves.
            return new NativeUiaTextSurface(_provider.OwnElementReference(element), node.Id);
        }

        // 2. A descendant that has one. Console hosts (conhost, Windows
        //    Terminal) put focus on the window while the text pattern lives on
        //    a child text area, so numpad review found no surface at all and
        //    silently did nothing. The same shape covers any framework that
        //    focuses a container rather than its text.
        if (element is not null && _provider.Automation is { } automation)
        {
            try
            {
                var condition = automation.CreatePropertyCondition(
                    UIA_PROPERTY_ID.UIA_IsTextPatternAvailablePropertyId, true);
                var textChild = element.FindFirst(TreeScope.TreeScope_Descendants, condition);
                if (textChild is not null)
                {
                    _log.Verbose("text surface for {NodeId}: descendant TextPattern", node.Id);
                    // Freshly marshalled by FindFirst, so this one is already
                    // ours; it needs owning, not duplicating.
                    return new NativeUiaTextSurface(_provider.OwnElement(textChild), node.Id);
                }
            }
            catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
            {
                // A subtree walk can fail on a hostile or dying provider;
                // the cheaper fallbacks below still apply.
            }
        }

        // 3. Win32 messages, for classic edits that expose no text pattern.
        var hwnd = Extra(node, "uia.NativeWindowHandle") is int h ? (nint)h : 0;
        if (hwnd != 0 && Win32Text.TryGetText(hwnd, out _))
        {
            _log.Verbose("text surface for {NodeId}: Win32 messages", node.Id);
            return new Win32TextSurface(hwnd, node.Id);
        }

        // 4. The node's own value. No caret, but review and say-all still work.
        if (!string.IsNullOrEmpty(node.Value))
        {
            _log.Verbose("text surface for {NodeId}: node value (read-only)", node.Id);
            return new StringTextSurface(node.Value, caretOffset: 0, nodeId: node.Id);
        }

        // A control with no text at all is not a failure; buttons exist.
        return null;
    }

    private static object? Extra(AccessibleNode node, string key)
        => node.Extras.TryGetValue(key, out var raw) ? raw : null;
}
