using System.Runtime.Versioning;
using System.Windows.Automation;

namespace Aura.Platform.Windows.Accessibility;

/// <summary>
/// The one <see cref="CacheRequest"/> every event subscription is registered
/// under, naming every property <see cref="UiaNodeMapper"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the difference between ~28 cross-process calls per focus event
/// and one.</b>
/// </para>
/// <para>
/// <see cref="AutomationElement.Current"/> is not a snapshot. It is a struct
/// whose every property getter issues a fresh <c>GetCurrentPropertyValue</c>
/// against the provider — a separate cross-process COM round trip each.
/// Mapping a node touches roughly two dozen properties, so a single focus
/// change cost two dozen serialised round trips before a word was composed.
/// Against an in-process WPF provider that is cheap; against Chromium, a JVM,
/// Electron, or Office it is milliseconds apiece and the whole 50 ms budget is
/// gone several times over.
/// </para>
/// <para>
/// When a handler is registered while a cache request is active, UIA bulk-fetches
/// the whole named set once, at event time, and ships it with the element.
/// Reading <c>element.Cached.X</c> afterwards is a local memory access.
/// </para>
/// <para>
/// It is also <em>more correct</em>. Cached values are the element's state at
/// the moment the event fired, not at the moment we got around to reading it.
/// The live path announces whatever the control happens to say a few
/// milliseconds later, which is its own class of "spoke the wrong value" bug.
/// </para>
/// <para>
/// <see cref="AutomationElementMode.Full"/> is required, not merely preferred:
/// <c>None</c> yields a detached snapshot that can no longer answer live calls,
/// and the caret path still needs <c>TextPattern</c> off the same element.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class UiaCache
{
    /// <summary>
    /// Native UIA property ids for set membership and tree depth (Windows 8.1+).
    /// <c>System.Windows.Automation</c> exposes no named properties for these,
    /// so they are looked up by id. Null on a platform that lacks them.
    /// </summary>
    internal static readonly AutomationProperty? PositionInSetProperty = AutomationProperty.LookupById(30152);
    internal static readonly AutomationProperty? SizeOfSetProperty = AutomationProperty.LookupById(30153);
    internal static readonly AutomationProperty? LevelProperty = AutomationProperty.LookupById(30154);

    internal static CacheRequest Request { get; } = Build();

    /// <summary>
    /// Run <paramref name="action"/> with the cache request active on this
    /// thread. Any event handler registered inside, and any element returned
    /// inside, carries the cache.
    /// </summary>
    /// <remarks>
    /// The activation is thread-local, so every registration site has to do
    /// this — registering a handler outside an active request silently gives
    /// you uncached elements and the old cost, with no error anywhere.
    /// </remarks>
    internal static void Activated(Action action)
    {
        using (Request.Activate())
        {
            action();
        }
    }

    private static CacheRequest Build()
    {
        var request = new CacheRequest
        {
            // Full keeps a live reference on the element so the caret path can
            // still reach TextPattern. AutomationElementMode.None would be
            // marginally faster and would break text reading entirely.
            AutomationElementMode = AutomationElementMode.Full,
            TreeScope = TreeScope.Element,
        };

        // --- identity and classification ---
        request.Add(AutomationElement.RuntimeIdProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.HelpTextProperty);

        // --- state ---
        request.Add(AutomationElement.HasKeyboardFocusProperty);
        request.Add(AutomationElement.IsKeyboardFocusableProperty);
        request.Add(AutomationElement.IsEnabledProperty);
        request.Add(AutomationElement.IsOffscreenProperty);
        request.Add(AutomationElement.IsPasswordProperty);
        request.Add(AutomationElement.IsRequiredForFormProperty);

        // --- app-module escape hatch, and the Win32 text fallback's HWND ---
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.ClassNameProperty);
        request.Add(AutomationElement.FrameworkIdProperty);
        request.Add(AutomationElement.ProcessIdProperty);
        request.Add(AutomationElement.NativeWindowHandleProperty);

        // Screen position. Two distinct controls essentially never share a
        // bounding rectangle, whereas one control re-firing its focus event
        // always does — which is what makes it the reliable half of the
        // focus-identity key. See UiaAccessibilityProvider.FocusKey.
        request.Add(AutomationElement.BoundingRectangleProperty);

        // --- pattern availability ---
        // Reading availability as plain booleans means the mapper never has to
        // materialise a pattern object, which is itself a marshalled call.
        request.Add(AutomationElement.IsTogglePatternAvailableProperty);
        request.Add(AutomationElement.IsSelectionItemPatternAvailableProperty);
        request.Add(AutomationElement.IsExpandCollapsePatternAvailableProperty);
        request.Add(AutomationElement.IsValuePatternAvailableProperty);
        request.Add(AutomationElement.IsRangeValuePatternAvailableProperty);
        request.Add(AutomationElement.IsTextPatternAvailableProperty);

        // --- pattern properties ---
        // The pattern itself is added alongside its properties; UIA wants the
        // pattern present for its properties to be cacheable.
        request.Add(TogglePattern.Pattern);
        request.Add(TogglePattern.ToggleStateProperty);

        request.Add(SelectionItemPattern.Pattern);
        request.Add(SelectionItemPattern.IsSelectedProperty);

        request.Add(ExpandCollapsePattern.Pattern);
        request.Add(ExpandCollapsePattern.ExpandCollapseStateProperty);

        request.Add(ValuePattern.Pattern);
        request.Add(ValuePattern.ValueProperty);
        request.Add(ValuePattern.IsReadOnlyProperty);

        request.Add(RangeValuePattern.Pattern);
        request.Add(RangeValuePattern.ValueProperty);

        // TextPattern is deliberately NOT cached. Its value is in ranges, which
        // are not cacheable, and the caret path acquires it live anyway.

        // --- set membership / depth ---
        AddIfSupported(request, PositionInSetProperty);
        AddIfSupported(request, SizeOfSetProperty);
        AddIfSupported(request, LevelProperty);

        return request;
    }

    private static void AddIfSupported(CacheRequest request, AutomationProperty? property)
    {
        if (property is not null)
        {
            request.Add(property);
        }
    }
}
