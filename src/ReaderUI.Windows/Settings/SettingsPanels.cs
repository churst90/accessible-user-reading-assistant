using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Label = System.Windows.Controls.Label;
using Button = System.Windows.Controls.Button;
using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;
using DataGrid = System.Windows.Controls.DataGrid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Aura.UI.Settings;

/// <summary>
/// Builds each category's right-pane content. We assemble panels in code
/// rather than separate XAML files so the dialog is one self-contained unit
/// — easier to evolve while the settings list is small.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal static class SettingsPanels
{
    public static FrameworkElement BuildGeneralPanel(SettingsViewModel vm)
    {
        var panel = NewPanel("General");

        AddRow(panel, "Profile:", BuildProfileEditor(vm));
        panel.Children.Add(BuildCheck("Start Aura with Windows", nameof(SettingsViewModel.StartWithWindows)));

        panel.Children.Add(new TextBlock
        {
            Text = "Profiles let you keep separate configurations (work, gaming, accessible mode) and switch between them without editing JSON. Use 'default' for the standard user layer.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0),
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "App-specific overrides can be edited at %AppData%\\Aura\\apps\\<exe>\\config.json. They apply automatically when that app gains focus.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Tip: press Insert+O (or CapsLock+O on the laptop layout) at any time to reopen this dialog.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
        });
        return Wrap(panel);
    }

    private static System.Windows.Controls.TextBox BuildProfileEditor(SettingsViewModel vm)
    {
        var box = new System.Windows.Controls.TextBox();
        AutomationProperties.SetName(box, "Profile name");
        box.SetBinding(System.Windows.Controls.TextBox.TextProperty, new Binding(nameof(SettingsViewModel.Profile))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
        });
        return box;
    }

    public static FrameworkElement BuildSpeechPanel(SettingsViewModel vm)
    {
        var panel = NewPanel("Speech");

        // Labels follow NVDA's wording wherever NVDA has a name for the same
        // thing. A switching user should not have to work out that "Pitch
        // delta (semitones)" is the slider they know as "Pitch", and a unit
        // in the label is an implementation detail leaking into the interface.
        AddRow(panel, "Voice:", BuildVoiceSelector(vm));
        AddRow(panel, "Rate:", BuildSlider(nameof(SettingsViewModel.RatePercent), 25, 400, 5));
        AddRow(panel, "Pitch:", BuildSlider(nameof(SettingsViewModel.PitchDelta), -12, 12, 1));
        AddRow(panel, "Volume:", BuildSlider(nameof(SettingsViewModel.VolumeDelta), -100, 100, 5));

        return Wrap(panel);
    }

    /// <summary>
    /// A section heading. Its own method so every panel groups identically —
    /// the alternative is a hand-built TextBlock per section, which is how
    /// three of them ended up with different margins and one with no heading.
    /// </summary>
    private static TextBlock Group(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 16, 0, 4),
        FontWeight = FontWeights.SemiBold,
    };

    public static FrameworkElement BuildKeyboardPanel(SettingsViewModel vm)
    {
        var panel = NewPanel("Keyboard");

        AddRow(panel, "Layout:", BuildLayoutSelector(vm));

        panel.Children.Add(Group("Speak typed keys"));

        // Order is deliberate and was asked for by ear: modifiers, then
        // characters, then words, then the Read-mode question — because that is
        // increasing scope, and the last one is a qualifier on the two above it
        // rather than a fourth thing to turn on.
        panel.Children.Add(BuildCheck(
            "Speak command keys",
            nameof(SettingsViewModel.SpeakCommandKeys)));
        panel.Children.Add(BuildCheck(
            "Speak typed characters",
            nameof(SettingsViewModel.SpeakCharacters)));
        panel.Children.Add(BuildCheck(
            "Speak typed words",
            nameof(SettingsViewModel.SpeakWords)));

        // Independent checkboxes rather than NVDA's off/characters/words/both
        // dropdown: "both" and "off" are just the two combinations of two
        // booleans, and a four-way list makes the user translate their intent
        // into someone else's enumeration.
        //
        // Command keys are deliberately NOT covered by this: a modifier is a
        // modifier in every mode, and a user who wants to hear Control wants to
        // hear it while reading too.
        panel.Children.Add(BuildCheck(
            "Also speak characters and words in Read mode",
            nameof(SettingsViewModel.ApplyEchoInReadMode)));

        // Its own group: deleting is not typing. Grouping it with the echo
        // checkboxes implies turning character echo off also silences
        // deletions, which is the opposite of what a user wants.
        // Deleting used to be a checkbox. It is not a preference: a reader that
        // silently discards what you just removed has failed at the one job
        // that keystroke has. Always on.

        panel.Children.Add(new TextBlock
        {
            Text = "Desktop layout uses Insert as the Reader modifier and the numeric keypad " +
                   "(NumLock ON) for review-cursor navigation. Numpad keys are intercepted so they " +
                   "review without moving the system caret.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0),
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Laptop layout uses CapsLock as the Reader modifier and the main arrow cluster " +
                   "for review (CapsLock+Arrows for character/line, CapsLock+Ctrl+Arrows for word).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
        });

        return Wrap(panel);
    }

    public static FrameworkElement BuildKeybindingsPanel(SettingsViewModel vm, Window owner)
    {
        var panel = NewPanel("Key bindings");

        panel.Children.Add(new TextBlock
        {
            Text = "Click a row's chord and press Change... to rebind. Use Clear in the capture dialog to remove a binding.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
        });

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = vm.KeyBindings,
            MinHeight = 320,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            IsReadOnly = true,
        };
        AutomationProperties.SetName(grid, "Keybinding list");

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Command",
            Binding = new Binding(nameof(KeyBindingRow.CommandLabel)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Chord",
            Binding = new Binding(nameof(KeyBindingRow.Chord)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });

        var changeButton = new Button
        {
            Content = "Change…",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 100,
            IsEnabled = false,
        };
        AutomationProperties.SetName(changeButton, "Change selected binding");

        grid.SelectionChanged += (_, _) =>
        {
            changeButton.IsEnabled = grid.SelectedItem is KeyBindingRow;
        };

        grid.MouseDoubleClick += (_, _) => Change();
        changeButton.Click += (_, _) => Change();

        panel.Children.Add(grid);
        panel.Children.Add(changeButton);

        return Wrap(panel);

        void Change()
        {
            if (grid.SelectedItem is not KeyBindingRow row)
            {
                return;
            }
            var dialog = new ChordCaptureDialog { Owner = owner };
            if (dialog.ShowDialog() == true)
            {
                row.Chord = dialog.CapturedChord ?? string.Empty;
            }
        }
    }

    public static FrameworkElement BuildPlaceholder(string title)
    {
        var panel = NewPanel(title);
        panel.Children.Add(new TextBlock
        {
            Text = $"{title} settings are coming soon.",
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
        });
        return Wrap(panel);
    }

    private static StackPanel NewPanel(string title)
    {
        // A layout container must never be a tab stop. WPF makes panels
        // focusable by default, so tabbing landed on an unnamed StackPanel
        // that announced nothing — the "tab, silence, tab again" dead step in
        // the focus order.
        var panel = new StackPanel { Focusable = false, IsHitTestVisible = true };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        AutomationProperties.SetName(panel, $"{title} settings");
        return panel;
    }

    private static ScrollViewer Wrap(StackPanel panel) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = panel,
    };

    private static ComboBox BuildVoiceSelector(SettingsViewModel vm)
    {
        var combo = new ComboBox { IsEditable = false };
        AutomationProperties.SetName(combo, "Voice");
        combo.ItemsSource = vm.AvailableVoices;
        combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SettingsViewModel.VoiceId))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            FallbackValue = string.Empty,
        });
        return combo;
    }

    private static ComboBox BuildLayoutSelector(SettingsViewModel vm)
    {
        var combo = new ComboBox();
        AutomationProperties.SetName(combo, "Layout");
        combo.ItemsSource = vm.AvailableLayouts;
        combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SettingsViewModel.Layout))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        return combo;
    }

    private static Grid BuildSlider(string boundProperty, double min, double max, double tickFrequency)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = tickFrequency,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, boundProperty);
        slider.SetBinding(Slider.ValueProperty, new Binding(boundProperty)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        Grid.SetColumn(slider, 0);
        grid.Children.Add(slider);

        var readout = new TextBlock
        {
            Width = 60,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
        };
        readout.SetBinding(TextBlock.TextProperty, new Binding(boundProperty)
        {
            Mode = BindingMode.OneWay,
            StringFormat = "{0:0}",
            ConverterCulture = CultureInfo.InvariantCulture,
        });
        Grid.SetColumn(readout, 1);
        grid.Children.Add(readout);

        return grid;
    }

    private static CheckBox BuildCheck(string label, string boundProperty)
    {
        var check = new CheckBox { Content = label };
        AutomationProperties.SetName(check, label);
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding(boundProperty)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        return check;
    }

    private static void AddRow(StackPanel panel, string label, FrameworkElement editor)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelControl = new Label { Content = label, Target = editor };
        Grid.SetColumn(labelControl, 0);
        Grid.SetColumn(editor, 1);
        row.Children.Add(labelControl);
        row.Children.Add(editor);
        panel.Children.Add(row);
    }
}
