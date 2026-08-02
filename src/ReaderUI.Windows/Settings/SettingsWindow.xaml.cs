using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using Aura.Config;

namespace Aura.UI.Settings;

[SupportedOSPlatform("windows6.1")]
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly Action<ReaderConfig> _save;

    public SettingsWindow(ReaderConfig current, IReadOnlyList<string> availableVoices, Action<ReaderConfig> save)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(save);
        // Build the view-model BEFORE InitializeComponent. The ListBox declares
        // SelectedIndex="0" in XAML, which fires SelectionChanged during the
        // XAML load — and the handler needs _viewModel to populate the panel.
        _viewModel = new SettingsViewModel(current, availableVoices);
        _save = save;
        InitializeComponent();
        DataContext = _viewModel;
        ShowCategory(SettingsCategory.General);
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectedIndex="0" in XAML fires this handler during EndInit, BEFORE
        // the auto-generated Connect() call has assigned the named fields
        // (CategoryList, DetailHost). Use `sender` for the list and guard
        // DetailHost; the constructor calls ShowCategory(General) explicitly
        // after InitializeComponent so the initial render is correct anyway.
        if (DetailHost is null || sender is not System.Windows.Controls.ListBox list)
        {
            return;
        }
        if (list.SelectedItem is not ListBoxItem item || item.Tag is not SettingsCategory category)
        {
            return;
        }
        ShowCategory(category);
    }

    private void ShowCategory(SettingsCategory category)
    {
        if (DetailHost is null)
        {
            return;
        }
        DetailHost.Content = category switch
        {
            SettingsCategory.General => SettingsPanels.BuildGeneralPanel(_viewModel),
            SettingsCategory.Speech => SettingsPanels.BuildSpeechPanel(_viewModel),
            SettingsCategory.Keyboard => SettingsPanels.BuildKeyboardPanel(_viewModel),
            SettingsCategory.Keybindings => SettingsPanels.BuildKeybindingsPanel(_viewModel, this),
            SettingsCategory.ReviewCursor => SettingsPanels.BuildPlaceholder("Review cursor"),
            SettingsCategory.Braille => SettingsPanels.BuildPlaceholder("Braille"),
            SettingsCategory.Mouse => SettingsPanels.BuildPlaceholder("Mouse"),
            _ => SettingsPanels.BuildPlaceholder(category.ToString()),
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Apply();
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        Apply();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Note: do NOT set DialogResult — the window is opened with Show(),
        // not ShowDialog(), so DialogResult would throw InvalidOperationException.
        Close();
    }

    private void Apply()
    {
        _save(_viewModel.ToConfig());
    }
}
