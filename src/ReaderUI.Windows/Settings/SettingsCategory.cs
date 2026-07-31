namespace OpenReader.UI.Settings;

/// <summary>
/// Identity of a settings category. The order is the display order in the
/// list pane; categories without a panel are still listed (showing a
/// "Coming soon" stub) so users discover what's planned.
/// </summary>
public enum SettingsCategory
{
    General = 0,
    Speech,
    Keyboard,
    Keybindings,
    ReviewCursor,
    Braille,
    Mouse,
}
