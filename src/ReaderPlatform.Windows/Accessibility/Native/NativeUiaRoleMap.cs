using System.Runtime.Versioning;
using Aura.Abstractions.Accessibility;
using Windows.Win32.UI.Accessibility;

namespace Aura.Platform.Windows.Accessibility.Native;

/// <summary>
/// Native UIA control-type id → <see cref="AccessibleRole"/>.
/// </summary>
/// <remarks>
/// Deliberately keyed on the raw <see cref="UIA_CONTROLTYPE_ID"/> rather than
/// the managed <c>ControlType</c> class, so nothing here depends on
/// <c>System.Windows.Automation</c> being present. The mapping itself is the
/// same as <c>UiaRoleMap</c>'s and is the one place that knows about UIA
/// control-type identity.
/// </remarks>
// windows6.1 rather than bare "windows": the native UIA COM surface is
// annotated 6.1+, and an unversioned claim asserts support back to XP.
[SupportedOSPlatform("windows6.1")]
internal static class NativeUiaRoleMap
{
    private static readonly Dictionary<int, AccessibleRole> Map = new()
    {
        [(int)UIA_CONTROLTYPE_ID.UIA_WindowControlTypeId] = AccessibleRole.Window,
        [(int)UIA_CONTROLTYPE_ID.UIA_PaneControlTypeId] = AccessibleRole.Pane,
        [(int)UIA_CONTROLTYPE_ID.UIA_GroupControlTypeId] = AccessibleRole.Group,
        [(int)UIA_CONTROLTYPE_ID.UIA_TabControlTypeId] = AccessibleRole.Tab,
        [(int)UIA_CONTROLTYPE_ID.UIA_TabItemControlTypeId] = AccessibleRole.TabItem,
        [(int)UIA_CONTROLTYPE_ID.UIA_SplitButtonControlTypeId] = AccessibleRole.SplitButton,
        [(int)UIA_CONTROLTYPE_ID.UIA_StatusBarControlTypeId] = AccessibleRole.StatusBar,
        [(int)UIA_CONTROLTYPE_ID.UIA_ToolBarControlTypeId] = AccessibleRole.ToolBar,
        [(int)UIA_CONTROLTYPE_ID.UIA_MenuBarControlTypeId] = AccessibleRole.MenuBar,
        [(int)UIA_CONTROLTYPE_ID.UIA_MenuControlTypeId] = AccessibleRole.Menu,
        [(int)UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId] = AccessibleRole.MenuItem,
        [(int)UIA_CONTROLTYPE_ID.UIA_TreeControlTypeId] = AccessibleRole.Tree,
        [(int)UIA_CONTROLTYPE_ID.UIA_TreeItemControlTypeId] = AccessibleRole.TreeItem,
        [(int)UIA_CONTROLTYPE_ID.UIA_ListControlTypeId] = AccessibleRole.List,
        [(int)UIA_CONTROLTYPE_ID.UIA_ListItemControlTypeId] = AccessibleRole.ListItem,
        [(int)UIA_CONTROLTYPE_ID.UIA_TableControlTypeId] = AccessibleRole.Table,
        [(int)UIA_CONTROLTYPE_ID.UIA_DataItemControlTypeId] = AccessibleRole.Row,
        [(int)UIA_CONTROLTYPE_ID.UIA_DataGridControlTypeId] = AccessibleRole.Table,
        [(int)UIA_CONTROLTYPE_ID.UIA_HeaderItemControlTypeId] = AccessibleRole.ColumnHeader,
        [(int)UIA_CONTROLTYPE_ID.UIA_HeaderControlTypeId] = AccessibleRole.Group,
        [(int)UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId] = AccessibleRole.Button,
        [(int)UIA_CONTROLTYPE_ID.UIA_CheckBoxControlTypeId] = AccessibleRole.CheckBox,
        [(int)UIA_CONTROLTYPE_ID.UIA_RadioButtonControlTypeId] = AccessibleRole.RadioButton,
        [(int)UIA_CONTROLTYPE_ID.UIA_ComboBoxControlTypeId] = AccessibleRole.ComboBox,
        [(int)UIA_CONTROLTYPE_ID.UIA_SpinnerControlTypeId] = AccessibleRole.Spinner,
        [(int)UIA_CONTROLTYPE_ID.UIA_SliderControlTypeId] = AccessibleRole.Slider,
        [(int)UIA_CONTROLTYPE_ID.UIA_ProgressBarControlTypeId] = AccessibleRole.ProgressBar,
        [(int)UIA_CONTROLTYPE_ID.UIA_HyperlinkControlTypeId] = AccessibleRole.Hyperlink,
        [(int)UIA_CONTROLTYPE_ID.UIA_EditControlTypeId] = AccessibleRole.Edit,
        [(int)UIA_CONTROLTYPE_ID.UIA_DocumentControlTypeId] = AccessibleRole.Document,
        [(int)UIA_CONTROLTYPE_ID.UIA_TextControlTypeId] = AccessibleRole.StaticText,
        [(int)UIA_CONTROLTYPE_ID.UIA_ImageControlTypeId] = AccessibleRole.Image,
        [(int)UIA_CONTROLTYPE_ID.UIA_ToolTipControlTypeId] = AccessibleRole.ToolTip,
        [(int)UIA_CONTROLTYPE_ID.UIA_CustomControlTypeId] = AccessibleRole.Custom,
        [(int)UIA_CONTROLTYPE_ID.UIA_SeparatorControlTypeId] = AccessibleRole.Custom,
        [(int)UIA_CONTROLTYPE_ID.UIA_ThumbControlTypeId] = AccessibleRole.Custom,
        [(int)UIA_CONTROLTYPE_ID.UIA_ScrollBarControlTypeId] = AccessibleRole.Custom,
        [(int)UIA_CONTROLTYPE_ID.UIA_TitleBarControlTypeId] = AccessibleRole.Group,
        [(int)UIA_CONTROLTYPE_ID.UIA_CalendarControlTypeId] = AccessibleRole.Table,
        [(int)UIA_CONTROLTYPE_ID.UIA_AppBarControlTypeId] = AccessibleRole.ToolBar,
        [(int)UIA_CONTROLTYPE_ID.UIA_SemanticZoomControlTypeId] = AccessibleRole.Group,
    };

    /// <summary>
    /// Map a control-type id. Unknown ids become
    /// <see cref="AccessibleRole.Custom"/> rather than throwing — a provider
    /// inventing a control type must not silence the reader.
    /// </summary>
    internal static AccessibleRole ToRole(int controlTypeId)
        => Map.TryGetValue(controlTypeId, out var role) ? role : AccessibleRole.Custom;
}
