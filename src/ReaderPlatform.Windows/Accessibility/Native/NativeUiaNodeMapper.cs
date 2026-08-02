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
        // No runtime id means the element cannot be correlated across events.
        // A fresh guid keeps it addressable for this one announcement without
        // ever matching a later one — the honest outcome.
        return new NodeId(Guid.NewGuid().ToString("N"));
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
        // Last resort. Combo boxes commonly report their selected entry only
        // through the legacy bridge, so without this one tabbing past a combo
        // announced its label and nothing else.
        return GetString(element, UIA_PROPERTY_ID.UIA_LegacyIAccessibleValuePropertyId, cached);
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
