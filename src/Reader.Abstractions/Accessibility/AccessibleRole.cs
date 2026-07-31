namespace OpenReader.Abstractions.Accessibility;

/// <summary>
/// Normalized role identifier for an accessibility tree node.
/// Maps from UIA <c>ControlType</c> on Windows and AT-SPI <c>Role</c> on Linux.
/// </summary>
/// <remarks>
/// Inspired by ARIA roles and UIA control types. Keep this list deliberately
/// small — exotic roles map to <see cref="Custom"/> with the platform-specific
/// identifier carried in <c>AccessibleNode.Extras</c>.
/// </remarks>
public enum AccessibleRole
{
    Unknown = 0,

    // Containers
    Window,
    Dialog,
    Pane,
    Group,
    Tab,
    TabItem,
    SplitPane,
    ScrollPane,
    StatusBar,
    ToolBar,
    MenuBar,
    Menu,
    MenuItem,
    ContextMenu,
    Tree,
    TreeItem,
    List,
    ListItem,
    Table,
    Row,
    Cell,
    ColumnHeader,
    RowHeader,

    // Interactive
    Button,
    ToggleButton,
    SplitButton,
    CheckBox,
    RadioButton,
    ComboBox,
    Spinner,
    Slider,
    ProgressBar,
    Hyperlink,

    // Text
    Edit,
    PasswordEdit,
    Document,
    Heading,
    Paragraph,
    StaticText,

    // Media
    Image,
    Audio,
    Video,

    // Notifications
    ToolTip,
    Notification,
    Alert,

    // Custom / unmapped
    Custom,
}
