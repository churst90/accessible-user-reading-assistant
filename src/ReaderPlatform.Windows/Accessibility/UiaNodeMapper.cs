using System.Globalization;
using System.Runtime.Versioning;
using System.Windows.Automation;
using Aura.Abstractions.Accessibility;

namespace Aura.Platform.Windows.Accessibility;

/// <summary>
/// Converts a UIA <see cref="AutomationElement"/> snapshot to an immutable
/// <see cref="AccessibleNode"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every value is read as a <em>property</em>, never through a pattern object.
/// Materialising a pattern (<c>TryGetCurrentPattern</c>) is itself a marshalled
/// call, and then each <c>pattern.Current.X</c> off it is another; asking the
/// element for <c>IsTogglePatternAvailableProperty</c> and
/// <c>ToggleStateProperty</c> instead gets the same answers out of the batch
/// that <see cref="UiaCache"/> already fetched.
/// </para>
/// <para>
/// Prefer <see cref="MapCached"/> for elements that arrived from an event —
/// they carry the cache, so the whole mapping costs no round trips at all.
/// <see cref="Map"/> is the live path, for hit-testing and the root, where
/// there is no cache to read.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class UiaNodeMapper
{
    /// <summary>
    /// Map an element that arrived from a cached event subscription. Falls back
    /// to the live path if the cache turns out to be missing — a provider that
    /// ignores cache requests should degrade in speed, not break.
    /// </summary>
    public static AccessibleNode? MapCached(AutomationElement? element)
    {
        if (element is null)
        {
            return null;
        }
        try
        {
            return Build(new PropertySource(element, cached: true));
        }
        catch (InvalidOperationException)
        {
            // Raised when a property was not in the cache request. One catch
            // for the whole mapping rather than one per property: this is the
            // "provider didn't honour the cache" path, not the hot path.
            return Map(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>Map an element with live reads. Costs one round trip per property.</summary>
    public static AccessibleNode? Map(AutomationElement? element)
    {
        if (element is null)
        {
            return null;
        }
        try
        {
            return Build(new PropertySource(element, cached: false));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static AccessibleNode Build(PropertySource source)
    {
        var role = UiaRoleMap.ToRole(source.Get(AutomationElement.ControlTypeProperty) as ControlType);
        var name = AsString(source.Get(AutomationElement.NameProperty));
        var description = AsString(source.Get(AutomationElement.HelpTextProperty));

        return new AccessibleNode(
            id: BuildId(source),
            role: role,
            name: name,
            value: ReadValue(source),
            description: description,
            states: ReadStates(source),
            parentId: null,
            childrenFactory: null,
            extras: BuildExtras(source));
    }

    private static NodeId BuildId(PropertySource source)
    {
        if (source.Get(AutomationElement.RuntimeIdProperty) is int[] { Length: > 0 } runtimeId)
        {
            return new NodeId(string.Join('.', runtimeId.Select(i => i.ToString(CultureInfo.InvariantCulture))));
        }
        // No runtime id means the element cannot be correlated across events.
        // A fresh guid keeps it addressable for this one announcement without
        // ever matching a later one — which is the honest outcome.
        return new NodeId(Guid.NewGuid().ToString("N"));
    }

    private static AccessibleStates ReadStates(PropertySource source)
    {
        var states = AccessibleStates.None;

        if (source.GetBool(AutomationElement.HasKeyboardFocusProperty))
        {
            states |= AccessibleStates.Focused;
        }
        if (source.GetBool(AutomationElement.IsKeyboardFocusableProperty))
        {
            states |= AccessibleStates.Focusable;
        }
        if (!source.GetBool(AutomationElement.IsEnabledProperty, defaultValue: true))
        {
            states |= AccessibleStates.Disabled;
        }
        if (source.GetBool(AutomationElement.IsOffscreenProperty))
        {
            states |= AccessibleStates.Offscreen;
        }
        if (source.GetBool(AutomationElement.IsPasswordProperty))
        {
            states |= AccessibleStates.Protected;
        }
        if (source.GetBool(AutomationElement.IsRequiredForFormProperty))
        {
            states |= AccessibleStates.Required;
        }

        if (source.GetBool(AutomationElement.IsTogglePatternAvailableProperty)
            && source.Get(TogglePattern.ToggleStateProperty) is ToggleState toggle)
        {
            states |= toggle switch
            {
                ToggleState.On => AccessibleStates.Checked,
                ToggleState.Indeterminate => AccessibleStates.Mixed,
                _ => AccessibleStates.None,
            };
        }

        if (source.GetBool(AutomationElement.IsSelectionItemPatternAvailableProperty))
        {
            states |= AccessibleStates.Selectable;
            if (source.GetBool(SelectionItemPattern.IsSelectedProperty))
            {
                states |= AccessibleStates.Selected;
            }
        }

        if (source.GetBool(AutomationElement.IsExpandCollapsePatternAvailableProperty))
        {
            states |= AccessibleStates.Expandable;
            if (source.Get(ExpandCollapsePattern.ExpandCollapseStateProperty) is ExpandCollapseState expand)
            {
                states |= expand switch
                {
                    ExpandCollapseState.Expanded => AccessibleStates.Expanded,
                    ExpandCollapseState.Collapsed => AccessibleStates.Collapsed,
                    _ => AccessibleStates.None,
                };
            }
        }

        if (source.GetBool(AutomationElement.IsValuePatternAvailableProperty)
            && source.GetBool(ValuePattern.IsReadOnlyProperty))
        {
            states |= AccessibleStates.ReadOnly;
        }

        return states;
    }

    private static string? ReadValue(PropertySource source)
    {
        if (source.GetBool(AutomationElement.IsValuePatternAvailableProperty))
        {
            var raw = AsString(source.Get(ValuePattern.ValueProperty));
            if (raw is not null)
            {
                return raw;
            }
        }
        if (source.GetBool(AutomationElement.IsRangeValuePatternAvailableProperty)
            && source.Get(RangeValuePattern.ValueProperty) is double d)
        {
            return d.ToString("0.##", CultureInfo.CurrentCulture);
        }
        return null;
    }

    private static Dictionary<string, object?>? BuildExtras(PropertySource source)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        AddString(dict, "uia.AutomationId", source.Get(AutomationElement.AutomationIdProperty));
        AddString(dict, "uia.ClassName", source.Get(AutomationElement.ClassNameProperty));
        AddString(dict, "uia.FrameworkId", source.Get(AutomationElement.FrameworkIdProperty));

        if (source.Get(AutomationElement.ProcessIdProperty) is int pid and > 0)
        {
            dict["uia.ProcessId"] = pid;
        }

        // Carried so the Win32 text fallback doesn't have to make a live call
        // back to the provider just to learn the window handle.
        if (source.Get(AutomationElement.NativeWindowHandleProperty) is int hwnd and not 0)
        {
            dict["uia.NativeWindowHandle"] = hwnd;
        }

        if (source.GetBool(AutomationElement.IsTextPatternAvailableProperty))
        {
            dict["uia.HasTextPattern"] = true;
        }

        if (source.Get(AutomationElement.BoundingRectangleProperty) is System.Windows.Rect rect
            && !rect.IsEmpty
            && rect is { Width: > 0, Height: > 0 })
        {
            dict["uia.Bounds"] = string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)rect.X},{(int)rect.Y},{(int)rect.Width},{(int)rect.Height}");
        }

        // Surfaced into rule templates as {position}, {setSize}, {level}.
        AddPositiveInt(dict, "uia.PositionInSet", source, UiaCache.PositionInSetProperty);
        AddPositiveInt(dict, "uia.SizeOfSet", source, UiaCache.SizeOfSetProperty);
        AddPositiveInt(dict, "uia.Level", source, UiaCache.LevelProperty);

        return dict.Count == 0 ? null : dict;
    }

    private static void AddString(Dictionary<string, object?> dict, string key, object? raw)
    {
        if (AsString(raw) is { } value)
        {
            dict[key] = value;
        }
    }

    private static void AddPositiveInt(
        Dictionary<string, object?> dict,
        string key,
        PropertySource source,
        AutomationProperty? property)
    {
        if (property is not null && source.Get(property) is int value and > 0)
        {
            dict[key] = value;
        }
    }

    private static string? AsString(object? raw)
        => raw is string s && s.Length > 0 ? s : null;

    /// <summary>
    /// Reads properties from either the element's event-time cache or live,
    /// so the mapping above is written once.
    /// </summary>
    private readonly struct PropertySource(AutomationElement element, bool cached)
    {
        private readonly AutomationElement _element = element;
        private readonly bool _cached = cached;

        /// <summary>
        /// A property value, or <c>null</c> when the element does not support
        /// it. <see cref="InvalidOperationException"/> is allowed to escape in
        /// cached mode — it means the property was missing from the cache
        /// request, which is a bug in <see cref="UiaCache"/> rather than a
        /// runtime condition, and <see cref="MapCached"/> catches it once for
        /// the whole mapping.
        /// </summary>
        public object? Get(AutomationProperty property)
        {
            try
            {
                var raw = _cached
                    ? _element.GetCachedPropertyValue(property, ignoreDefaultValue: true)
                    : _element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
                return ReferenceEquals(raw, AutomationElement.NotSupported) ? null : raw;
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
            catch (InvalidOperationException) when (!_cached)
            {
                // Live reads can transiently fail while a provider is
                // switching state. Absent is the right answer.
                return null;
            }
        }

        public bool GetBool(AutomationProperty property, bool defaultValue = false)
            => Get(property) is bool b ? b : defaultValue;
    }
}
