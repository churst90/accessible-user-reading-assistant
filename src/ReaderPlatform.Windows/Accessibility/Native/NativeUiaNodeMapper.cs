using System.Globalization;
using System.Runtime.Versioning;
using Aura.Abstractions.Accessibility;
using Windows.Win32.UI.Accessibility;

namespace Aura.Platform.Windows.Accessibility.Native;

/// <summary>
/// Converts a native <see cref="IUIAutomationElement"/> into an
/// <see cref="AccessibleNode"/>.
/// </summary>
/// <remarks>
/// Reads only properties, never pattern objects. Materialising a pattern is
/// itself a marshalled call and each read off it is another; asking the element
/// for <c>IsTogglePatternAvailable</c> and <c>ToggleToggleState</c> instead
/// gets the same answers out of the batch <see cref="NativeUia"/> already
/// fetched. When the element came from a cached subscription the whole mapping
/// costs no round trips at all.
/// </remarks>
// windows6.1 rather than bare "windows": the native UIA COM surface is
// annotated 6.1+, and an unversioned claim asserts support back to XP.
[SupportedOSPlatform("windows6.1")]
internal static class NativeUiaNodeMapper
{
    /// <summary>Map an element whose properties were cached at event time.</summary>
    internal static AccessibleNode? MapCached(IUIAutomationElement? element)
        => element is null ? null : Build(element, cached: true);

    /// <summary>Map with live reads — one round trip per property. For hit-testing and the root.</summary>
    internal static AccessibleNode? Map(IUIAutomationElement? element)
        => element is null ? null : Build(element, cached: false);

    private static AccessibleNode? Build(IUIAutomationElement element, bool cached)
    {
        try
        {
            var role = NativeUiaRoleMap.ToRole(GetInt(element, UIA_PROPERTY_ID.UIA_ControlTypePropertyId, cached));

            return new AccessibleNode(
                id: BuildId(element, cached),
                role: role,
                name: GetString(element, UIA_PROPERTY_ID.UIA_NamePropertyId, cached),
                value: ReadValue(element, cached),
                description: GetString(element, UIA_PROPERTY_ID.UIA_HelpTextPropertyId, cached),
                states: ReadStates(element, cached),
                parentId: null,
                childrenFactory: null,
                extras: BuildExtras(element, cached));
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            // The element went away between the event and the mapping. Routine.
            return null;
        }
    }

    private static NodeId BuildId(IUIAutomationElement element, bool cached)
    {
        // Read the runtime id as a property rather than through GetRuntimeId(),
        // which returns a raw SAFEARRAY and would need unsafe handling for no
        // benefit.
        if (Get(element, UIA_PROPERTY_ID.UIA_RuntimeIdPropertyId, cached) is int[] { Length: > 0 } runtimeId)
        {
            return new NodeId(string.Join('.', runtimeId.Select(i => i.ToString(CultureInfo.InvariantCulture))));
        }
        // No runtime id. A fresh guid used to go here, on the reasoning that an
        // element which cannot be correlated should not pretend to be. That was
        // wrong, and expensively so: three separate mechanisms correlate by id,
        // and all three fail open rather than closed.
        //
        //   OutputArbiter collapses two producers describing one action by
        //   comparing subjects — with unique ids nothing ever matches, so a
        //   focus event and a selection event for the same control both speak.
        //   That is "general, general".
        //
        //   FocusTracker decides whether a queued announcement is still about
        //   the focus by comparing ids — with unique ids the answer is always
        //   no, so announcements are swept that should have been kept, and the
        //   ones that survive are the ones nobody could match either way.
        //
        //   The provider's own focus dedup falls back to this id in its key.
        //
        // A composite of the properties that actually identify a control is
        // stable across events for the same control and distinct between
        // different ones, which is everything the correlation needs. It is the
        // same shape as FocusKey, deliberately.
        var role = Get(element, UIA_PROPERTY_ID.UIA_ControlTypePropertyId, cached) as int? ?? 0;
        var name = GetString(element, UIA_PROPERTY_ID.UIA_NamePropertyId, cached) ?? string.Empty;
        var automationId = GetString(element, UIA_PROPERTY_ID.UIA_AutomationIdPropertyId, cached) ?? string.Empty;
        var bounds = Get(element, UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId, cached) is double[] { Length: 4 } r
            ? string.Create(CultureInfo.InvariantCulture, $"{r[0]:0},{r[1]:0},{r[2]:0},{r[3]:0}")
            : string.Empty;
        return new NodeId(string.Create(CultureInfo.InvariantCulture,
            $"k:{role}|{name}|{automationId}|{bounds}"));
    }

    private static AccessibleStates ReadStates(IUIAutomationElement element, bool cached)
    {
        var states = AccessibleStates.None;

        if (GetBool(element, UIA_PROPERTY_ID.UIA_HasKeyboardFocusPropertyId, cached))
        {
            states |= AccessibleStates.Focused;
        }
        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsKeyboardFocusablePropertyId, cached))
        {
            states |= AccessibleStates.Focusable;
        }
        if (!GetBool(element, UIA_PROPERTY_ID.UIA_IsEnabledPropertyId, cached, defaultValue: true))
        {
            states |= AccessibleStates.Disabled;
        }
        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsOffscreenPropertyId, cached))
        {
            states |= AccessibleStates.Offscreen;
        }
        // UIA has no distinct password control type; this flag is the only
        // signal, and getting it wrong means reading a password aloud.
        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsPasswordPropertyId, cached))
        {
            states |= AccessibleStates.Protected;
        }
        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsRequiredForFormPropertyId, cached))
        {
            states |= AccessibleStates.Required;
        }

        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsTogglePatternAvailablePropertyId, cached))
        {
            states |= GetInt(element, UIA_PROPERTY_ID.UIA_ToggleToggleStatePropertyId, cached) switch
            {
                1 => AccessibleStates.Checked,       // ToggleState_On
                2 => AccessibleStates.Mixed,         // ToggleState_Indeterminate
                _ => AccessibleStates.None,
            };
        }

        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsSelectionItemPatternAvailablePropertyId, cached))
        {
            states |= AccessibleStates.Selectable;
            if (GetBool(element, UIA_PROPERTY_ID.UIA_SelectionItemIsSelectedPropertyId, cached))
            {
                states |= AccessibleStates.Selected;
            }
        }

        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsExpandCollapsePatternAvailablePropertyId, cached))
        {
            states |= AccessibleStates.Expandable;
            states |= GetInt(element, UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId, cached) switch
            {
                0 => AccessibleStates.Collapsed,     // ExpandCollapseState_Collapsed
                1 => AccessibleStates.Expanded,      // ExpandCollapseState_Expanded
                2 => AccessibleStates.Expanded,      // PartiallyExpanded
                _ => AccessibleStates.None,
            };
        }

        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsValuePatternAvailablePropertyId, cached)
            && GetBool(element, UIA_PROPERTY_ID.UIA_ValueIsReadOnlyPropertyId, cached))
        {
            states |= AccessibleStates.ReadOnly;
        }

        return states;
    }

    private static string? ReadValue(IUIAutomationElement element, bool cached)
    {
        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsValuePatternAvailablePropertyId, cached)
            && GetString(element, UIA_PROPERTY_ID.UIA_ValueValuePropertyId, cached) is { } text)
        {
            return text;
        }
        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsRangeValuePatternAvailablePropertyId, cached)
            && Get(element, UIA_PROPERTY_ID.UIA_RangeValueValuePropertyId, cached) is double d)
        {
            return d.ToString("0.##", CultureInfo.CurrentCulture);
        }
        // Combo boxes commonly report their selected entry only through the
        // legacy bridge.
        if (GetString(element, UIA_PROPERTY_ID.UIA_LegacyIAccessibleValuePropertyId, cached) is { Length: > 0 } legacy)
        {
            return legacy;
        }

        // And a WPF combo box reports it through neither. It has no value
        // pattern at all — what it has is a *selection*, and the selected item
        // is a separate element whose name is the text the user needs. Without
        // this, tabbing onto a combo announced its label and the word "combo
        // box" and left the user to open the list to find out what was in it.
        //
        // Gated on the cached pattern flag, and that gate matters more than it
        // looks. Every control without a value reaches this line — every
        // button, every label, every menu item — so an ungated live pattern
        // call here is a cross-process round trip on almost every focus change
        // in the system, to ask a question whose answer is nearly always "no
        // selection pattern". The flag is already in the cache request and
        // costs nothing.
        if (!GetBool(element, UIA_PROPERTY_ID.UIA_IsSelectionPatternAvailablePropertyId, cached))
        {
            return null;
        }
        return ReadSelectedItemName(element);
    }

    /// <summary>
    /// The name of the first selected child, for controls that express their
    /// value as a selection.
    /// </summary>
    /// <remarks>
    /// A live pattern call, so it costs a round trip — but only for a control
    /// that has already failed three cheaper cached lookups, which in practice
    /// means a combo box or list the user has just landed on. It is bounded by
    /// the client transaction timeout like every other call.
    /// </remarks>
    private static string? ReadSelectedItemName(IUIAutomationElement element)
    {
        try
        {
            if (element.GetCurrentPattern(UIA_PATTERN_ID.UIA_SelectionPatternId)
                is not IUIAutomationSelectionPattern selection)
            {
                return null;
            }
            var selected = selection.GetCurrentSelection();
            if (selected is null || selected.Length == 0)
            {
                return null;
            }
            var name = selected.GetElement(0)?.CurrentName;
            return name?.ToString();
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return null;
        }
    }

    private static Dictionary<string, object?>? BuildExtras(IUIAutomationElement element, bool cached)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        Add(dict, "uia.AutomationId", GetString(element, UIA_PROPERTY_ID.UIA_AutomationIdPropertyId, cached));
        Add(dict, "uia.ClassName", GetString(element, UIA_PROPERTY_ID.UIA_ClassNamePropertyId, cached));
        Add(dict, "uia.FrameworkId", GetString(element, UIA_PROPERTY_ID.UIA_FrameworkIdPropertyId, cached));

        var pid = GetInt(element, UIA_PROPERTY_ID.UIA_ProcessIdPropertyId, cached);
        if (pid > 0)
        {
            dict["uia.ProcessId"] = pid;
        }

        // Carried so the Win32 text fallback needs no live call back to the
        // provider just to learn the window handle.
        var hwnd = GetInt(element, UIA_PROPERTY_ID.UIA_NativeWindowHandlePropertyId, cached);
        if (hwnd != 0)
        {
            dict["uia.NativeWindowHandle"] = hwnd;
        }

        if (GetBool(element, UIA_PROPERTY_ID.UIA_IsTextPatternAvailablePropertyId, cached))
        {
            dict["uia.HasTextPattern"] = true;
        }

        // Screen position, used as half the focus-identity key so that two
        // distinct controls sharing a role and name are still told apart.
        if (Get(element, UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId, cached) is double[] { Length: 4 } r
            && r[2] > 0 && r[3] > 0)
        {
            dict["uia.Bounds"] = string.Create(CultureInfo.InvariantCulture,
                $"{(int)r[0]},{(int)r[1]},{(int)r[2]},{(int)r[3]}");
        }

        AddPositive(dict, "uia.PositionInSet", GetInt(element, UIA_PROPERTY_ID.UIA_PositionInSetPropertyId, cached));
        AddPositive(dict, "uia.SizeOfSet", GetInt(element, UIA_PROPERTY_ID.UIA_SizeOfSetPropertyId, cached));
        AddPositive(dict, "uia.Level", GetInt(element, UIA_PROPERTY_ID.UIA_LevelPropertyId, cached));

        return dict.Count == 0 ? null : dict;
    }

    private static void Add(Dictionary<string, object?> dict, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict[key] = value;
        }
    }

    private static void AddPositive(Dictionary<string, object?> dict, string key, int value)
    {
        if (value > 0)
        {
            dict[key] = value;
        }
    }

    // ---- property access ----------------------------------------------------

    private static object? Get(IUIAutomationElement element, UIA_PROPERTY_ID property, bool cached)
    {
        try
        {
            var raw = cached
                ? element.GetCachedPropertyValue(property)
                : element.GetCurrentPropertyValue(property);
            return raw;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return null;
        }
    }

    private static string? GetString(IUIAutomationElement element, UIA_PROPERTY_ID property, bool cached)
        => Get(element, property, cached) is string s && s.Length > 0 ? s : null;

    private static int GetInt(IUIAutomationElement element, UIA_PROPERTY_ID property, bool cached)
        => Get(element, property, cached) is int i ? i : 0;

    private static bool GetBool(
        IUIAutomationElement element,
        UIA_PROPERTY_ID property,
        bool cached,
        bool defaultValue = false)
        => Get(element, property, cached) switch
        {
            bool b => b,
            // UIA marshals BOOL as int through some providers.
            int i => i != 0,
            _ => defaultValue,
        };

    /// <summary>
    /// Exceptions meaning "the provider can no longer answer", as opposed to a
    /// defect on our side. <see cref="OutOfMemoryException"/> and friends are
    /// deliberately excluded — those must keep propagating.
    /// </summary>
    internal static bool IsProviderFailure(Exception ex)
        => ex is System.Runtime.InteropServices.COMException
            or InvalidCastException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException;
}
