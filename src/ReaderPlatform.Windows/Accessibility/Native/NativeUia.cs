using System.Runtime.Versioning;
using Windows.Win32.UI.Accessibility;

namespace Aura.Platform.Windows.Accessibility.Native;

/// <summary>
/// Bootstrap for the native UI Automation client: creates the automation
/// object and builds the one cache request every subscription registers under.
/// </summary>
/// <remarks>
/// <para>
/// This replaces <c>System.Windows.Automation</c>, which froze around Windows 7
/// and cannot express what a screen reader needs — notification and live-region
/// events, heading level and link text attributes, event coalescing. See
/// <c>ASSESSMENT.md</c> S2.
/// </para>
/// </remarks>
// windows6.1 rather than bare "windows": the native UIA COM surface is
// annotated 6.1+, and an unversioned claim asserts support back to XP.
[SupportedOSPlatform("windows6.1")]
internal static class NativeUia
{
    /// <summary>CLSID_CUIAutomation8 — the UIA 3+ client object.</summary>
    private static readonly Guid CUIAutomation8Clsid = new("e22ad333-b25f-460c-83d0-0581107395c9");

    /// <summary>
    /// Create the automation client. <c>CUIAutomation8</c> rather than
    /// <c>CUIAutomation</c> because only the former exposes
    /// <see cref="IUIAutomation6"/>, which carries the notification and
    /// active-text-position handlers.
    /// </summary>
    internal static IUIAutomation Create()
    {
        var type = Type.GetTypeFromCLSID(CUIAutomation8Clsid, throwOnError: true)
            ?? throw new InvalidOperationException("CUIAutomation8 is not registered on this system.");
        var automation = (IUIAutomation)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create the UI Automation client."));
        if (OperatingSystem.IsWindowsVersionAtLeast(8))
        {
            TrySetTimeouts(automation);
        }
        return automation;
    }

    /// <summary>How long to spend reaching a provider that may not be there.</summary>
    internal const int ConnectionTimeoutMs = 1000;

    /// <summary>How long to spend on one call to a provider that may be wedged.</summary>
    internal const int TransactionTimeoutMs = 2000;

    /// <summary>
    /// Bound every call to a provider. Without this the dispatch loop blocks
    /// forever against a hung application and the reader goes silent with no
    /// signal and no recovery — the failure <c>ASSESSMENT.md</c> S1 is about,
    /// on the one path its <c>SendMessageTimeout</c> fix did not cover.
    /// </summary>
    /// <remarks>
    /// The values are a starting point, not a measurement: two seconds is two
    /// orders of magnitude longer than a healthy call and shorter than a user
    /// will tolerate silence. Set them from data once <c>PerfTimer</c> is on
    /// this path — see <c>docs/foundation/F5-EVIDENCE.md</c>.
    /// </remarks>
    [SupportedOSPlatform("windows8.0")]
    private static void TrySetTimeouts(IUIAutomation automation)
    {
        try
        {
            if (automation is IUIAutomation2 two)
            {
                two.ConnectionTimeout = ConnectionTimeoutMs;
                two.TransactionTimeout = TransactionTimeoutMs;
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException
            or System.Runtime.InteropServices.COMException)
        {
            // A client that will not take the timeouts still works; it is just
            // unbounded, which is what the watchdog is for.
        }
    }

    /// <summary>
    /// Every property <see cref="NativeUiaNodeMapper"/> reads, fetched in one
    /// cross-process call at event time instead of one call per property.
    /// </summary>
    internal static readonly UIA_PROPERTY_ID[] CachedProperties =
    [
        UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId,
        UIA_PROPERTY_ID.UIA_ControlTypePropertyId,
        UIA_PROPERTY_ID.UIA_NamePropertyId,
        UIA_PROPERTY_ID.UIA_HelpTextPropertyId,

        UIA_PROPERTY_ID.UIA_HasKeyboardFocusPropertyId,
        UIA_PROPERTY_ID.UIA_IsKeyboardFocusablePropertyId,
        UIA_PROPERTY_ID.UIA_IsEnabledPropertyId,
        UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId,
        UIA_PROPERTY_ID.UIA_IsPasswordPropertyId,
        UIA_PROPERTY_ID.UIA_IsRequiredForFormPropertyId,

        UIA_PROPERTY_ID.UIA_AutomationIdPropertyId,
        UIA_PROPERTY_ID.UIA_ClassNamePropertyId,
        UIA_PROPERTY_ID.UIA_FrameworkIdPropertyId,
        UIA_PROPERTY_ID.UIA_ProcessIdPropertyId,
        UIA_PROPERTY_ID.UIA_NativeWindowHandlePropertyId,
        UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId,

        UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId,
        UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId,
        UIA_PROPERTY_ID.UIA_IsSelectionPatternAvailablePropertyId,
        UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId,
        UIA_PROPERTY_ID.UIA_IsValuePatternAvailablePropertyId,
        UIA_PROPERTY_ID.UIA_IsRangeValuePatternAvailablePropertyId,
        UIA_PROPERTY_ID.UIA_IsTextPatternAvailablePropertyId,

        UIA_PROPERTY_ID.UIA_ToggleToggleStatePropertyId,
        UIA_PROPERTY_ID.UIA_SelectionItemIsSelectedPropertyId,
        UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId,
        UIA_PROPERTY_ID.UIA_ValueValuePropertyId,
        UIA_PROPERTY_ID.UIA_ValueIsReadOnlyPropertyId,
        UIA_PROPERTY_ID.UIA_RangeValueValuePropertyId,
        // Combo boxes frequently expose the selected entry only here, which is
        // why tabbing past one announced no value until it was expanded.
        UIA_PROPERTY_ID.UIA_LegacyIAccessibleValuePropertyId,
        UIA_PROPERTY_ID.UIA_LegacyIAccessibleChildIdPropertyId,

        // Set membership and depth — "4 of 10", "level 2".
        UIA_PROPERTY_ID.UIA_PositionInSetPropertyId,
        UIA_PROPERTY_ID.UIA_SizeOfSetPropertyId,
        UIA_PROPERTY_ID.UIA_LevelPropertyId,
    ];

    /// <summary>
    /// Build the shared cache request.
    /// </summary>
    /// <remarks>
    /// <c>AutomationElementMode_Full</c> is required, not merely preferred: the
    /// <c>None</c> mode yields a detached snapshot that can no longer answer
    /// live calls, and the caret path still needs <c>TextPattern</c> off the
    /// same element.
    /// </remarks>
    internal static IUIAutomationCacheRequest BuildCacheRequest(IUIAutomation automation)
    {
        var request = automation.CreateCacheRequest();
        foreach (var property in CachedProperties)
        {
            request.AddProperty(property);
        }
        request.AutomationElementMode = AutomationElementMode.AutomationElementMode_Full;
        request.TreeScope = TreeScope.TreeScope_Element;
        return request;
    }

    /// <summary>
    /// Ask for event coalescing where the client supports it.
    /// </summary>
    /// <remarks>
    /// A busy web page can raise thousands of events a second. Without
    /// coalescing the dispatch loop drowns and latency collapses — this is one
    /// of the capabilities the managed client never had.
    /// </remarks>
    [SupportedOSPlatform("windows10.0.17763.0")]
    internal static void TryEnableCoalescing(IUIAutomation automation)
    {
        try
        {
            if (automation is IUIAutomation6 six)
            {
                six.CoalesceEvents = CoalesceEventsOptions.CoalesceEventsOptions_Enabled;
                six.ConnectionRecoveryBehavior =
                    ConnectionRecoveryBehaviorOptions.ConnectionRecoveryBehaviorOptions_Enabled;
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException
            or System.Runtime.InteropServices.COMException)
        {
            // Older Windows without IUIAutomation6. Everything else still works.
        }
    }
}
