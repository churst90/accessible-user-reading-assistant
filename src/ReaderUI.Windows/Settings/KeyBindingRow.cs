using System.ComponentModel;
using OpenReader.Input.Commands;

namespace OpenReader.UI.Settings;

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
