using System.ComponentModel;
using Aura.Input.Commands;

namespace Aura.UI.Settings;

/// <summary>One row in the rebind grid: a command paired with its current chord text.</summary>
public sealed class KeyBindingRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _chord;

    public KeyBindingRow(ReaderCommand command, string commandLabel, string chord)
    {
        Command = command;
        CommandLabel = commandLabel;
        _chord = chord;
    }

    public ReaderCommand Command { get; }
    public string CommandLabel { get; }

    /// <summary>
    /// What a screen reader announces for this row.
    /// </summary>
    /// <remarks>
    /// WPF derives a DataGrid row's accessible name from the bound item's
    /// <see cref="object.ToString"/>. Without this override the grid announced
    /// "Aura.UI.Settings.KeyBindingRow" for every row — the type name, which
    /// tells the user nothing and is the same for all of them.
    /// </remarks>
    public override string ToString()
        => string.IsNullOrWhiteSpace(_chord)
            ? $"{CommandLabel}, unassigned"
            : $"{CommandLabel}, {_chord}";

    public string Chord
    {
        get => _chord;
        set
        {
            if (_chord == value)
            {
                return;
            }
            _chord = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chord)));
        }
    }
}
