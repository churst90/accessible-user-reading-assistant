using System.Runtime.Versioning;
using System.Windows.Automation;
using Aura.Abstractions.Accessibility;

namespace Aura.Platform.Windows.Accessibility;

/// <summary>
/// Single source of truth for UIA <see cref="ControlType"/> → <see cref="AccessibleRole"/>
/// mapping. Keep adjustments here so the rest of the platform layer stays
/// agnostic of UIA control-type identity.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UiaRoleMap
{
    private static readonly Dictionary<ControlType, AccessibleRole> Map = new()
    {
        [ControlType.Window] = AccessibleRole.Window,
        [ControlType.Pane] = AccessibleRole.Pane,
        [ControlType.Group] = AccessibleRole.Group,
        [ControlType.Tab] = AccessibleRole.Tab,
        [ControlType.TabItem] = AccessibleRole.TabItem,
        [ControlType.SplitButton] = AccessibleRole.SplitButton,
        [ControlType.StatusBar] = AccessibleRole.StatusBar,
        [ControlType.ToolBar] = AccessibleRole.ToolBar,
        [ControlType.MenuBar] = AccessibleRole.MenuBar,
        [ControlType.Menu] = AccessibleRole.Menu,
        [ControlType.MenuItem] = AccessibleRole.MenuItem,
        [ControlType.Tree] = AccessibleRole.Tree,
        [ControlType.TreeItem] = AccessibleRole.TreeItem,
        [ControlType.List] = AccessibleRole.List,
        [ControlType.ListItem] = AccessibleRole.ListItem,
        [ControlType.Table] = AccessibleRole.Table,
        [ControlType.DataItem] = AccessibleRole.Row,
        [ControlType.DataGrid] = AccessibleRole.Table,
        [ControlType.HeaderItem] = AccessibleRole.ColumnHeader,
        [ControlType.Header] = AccessibleRole.Group,
        [ControlType.Button] = AccessibleRole.Button,
        [ControlType.CheckBox] = AccessibleRole.CheckBox,
        [ControlType.RadioButton] = AccessibleRole.RadioButton,
        [ControlType.ComboBox] = AccessibleRole.ComboBox,
        [ControlType.Spinner] = AccessibleRole.Spinner,
        [ControlType.Slider] = AccessibleRole.Slider,
        [ControlType.ProgressBar] = AccessibleRole.ProgressBar,
        [ControlType.Hyperlink] = AccessibleRole.Hyperlink,
        [ControlType.Edit] = AccessibleRole.Edit,
        [ControlType.Document] = AccessibleRole.Document,
        [ControlType.Text] = AccessibleRole.StaticText,
        [ControlType.Image] = AccessibleRole.Image,
        [ControlType.ToolTip] = AccessibleRole.ToolTip,
        [ControlType.Custom] = AccessibleRole.Custom,
    };

    public static AccessibleRole ToRole(ControlType? controlType)
    {
        if (controlType is null)
        {
            return AccessibleRole.Unknown;
        }
        return Map.TryGetValue(controlType, out var role) ? role : AccessibleRole.Unknown;
    }
}
