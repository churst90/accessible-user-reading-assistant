using System.Runtime.Versioning;
using System.Windows;

namespace Aura.UI.Dialogs;

/// <summary>
/// Modal-style synthesizer picker. Listed engines come from the host's known
/// <c>ISpeechEngine</c> registry (SAPI5 today; eSpeak-NG planned). Selection is
/// persisted by the host on OK.
/// </summary>
[SupportedOSPlatform("windows6.1")]
public partial class SynthesizerDialog : Window
{
    /// <summary>Engine identifier the user selected, or <c>null</c> on cancel.</summary>
    public string? SelectedEngineId { get; private set; }

    /// <summary>True after the user clicked OK; false on Cancel / X.</summary>
    public bool Confirmed { get; private set; }

    private readonly Dictionary<string, string> _displayToId;

    public SynthesizerDialog(IReadOnlyList<SynthesizerOption> options, string? currentEngineId)
    {
        ArgumentNullException.ThrowIfNull(options);
        InitializeComponent();
        _displayToId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            _displayToId[option.DisplayName] = option.Id;
            EngineCombo.Items.Add(option.DisplayName);
        }
        if (currentEngineId is not null)
        {
            var match = options.FirstOrDefault(o => string.Equals(o.Id, currentEngineId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                EngineCombo.SelectedItem = match.DisplayName;
            }
        }
        if (EngineCombo.SelectedIndex < 0 && EngineCombo.Items.Count > 0)
        {
            EngineCombo.SelectedIndex = 0;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (EngineCombo.SelectedItem is string display
            && _displayToId.TryGetValue(display, out var id))
        {
            SelectedEngineId = id;
            Confirmed = true;
        }
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}

/// <summary>One row of the synthesizer combo: a stable engine id and a user-facing label.</summary>
public sealed record SynthesizerOption(string Id, string DisplayName);
