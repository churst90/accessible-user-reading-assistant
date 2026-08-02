using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenReader.Config;
using OpenReader.Input.Commands;
using OpenReader.Input.Gestures;

namespace OpenReader.UI.Settings;

/// <summary>
/// Mutable, two-way-bindable mirror of <see cref="ReaderConfig"/> for the
/// settings dialog. Constructed from the current config; produces a new
/// <see cref="ReaderConfig"/> when the user clicks Save.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // General
    private string _profile = "default";
    private bool _startWithWindows;

    // Speech
    private string _voiceId = string.Empty;
    private float _ratePercent = 100f;
    private float _volumeDelta;
    private float _pitchDelta;

    // Keyboard
    private string _layout = "desktop";
    private bool _speakCommandKeys;
    private bool _speakCharacters;
    private bool _speakDeletedCharacters = true;
    private bool _speakWords = true;

    public IReadOnlyList<string> AvailableVoices { get; }

    public IReadOnlyList<string> AvailableLayouts { get; } = new[] { "desktop", "laptop" };

    /// <summary>Editable per-command chord rows for the Keybindings panel.</summary>
    public ObservableCollection<KeyBindingRow> KeyBindings { get; } = new();

    private readonly Dictionary<ReaderCommand, string> _defaultChordsByCommand = new();

    public SettingsViewModel(ReaderConfig source, IReadOnlyList<string> availableVoices)
    {
        ArgumentNullException.ThrowIfNull(source);
        AvailableVoices = availableVoices ?? Array.Empty<string>();

        if (source.General is { } gen)
        {
            _profile = gen.Profile ?? "default";
            _startWithWindows = gen.StartWithWindows ?? false;
        }

        if (source.Speech is { } s)
        {
            _voiceId = s.VoiceId ?? string.Empty;
            _ratePercent = s.RatePercent ?? 100f;
            _volumeDelta = s.VolumeDelta ?? 0f;
            _pitchDelta = s.PitchDelta ?? 0f;
        }
        if (source.Keyboard is { } k)
        {
            _layout = k.Layout ?? "desktop";
            _speakCommandKeys = k.SpeakCommandKeys ?? false;
            _speakCharacters = k.SpeakCharacters ?? false;
            _speakWords = k.SpeakWords ?? true;
            _speakDeletedCharacters = k.SpeakDeletedCharacters ?? true;
        }

        BuildKeyBindings(source.Input?.KeyBindings, ResolveLayout(_layout));
    }

    private void BuildKeyBindings(IReadOnlyDictionary<string, string>? overrides, KeyboardLayout layout)
    {
        // Reflect what the host actually does at startup: defaults from the
        // active layout, with user overrides on top. The override map is
        // chord-keyed; we want a command-keyed view, so we invert.
        _defaultChordsByCommand.Clear();
        var defaultMap = new GestureMap();
        GestureBindings.ApplyDefaults(defaultMap, layout);
        foreach (var (chord, command) in defaultMap.Snapshot())
        {
            // First default chord seen per command wins for display.
            if (!_defaultChordsByCommand.ContainsKey(command))
            {
                _defaultChordsByCommand[command] = KeyChordParser.Format(chord);
            }
        }

        // User overrides may bind a different chord to a command. We want to
        // show the user's chord if any, otherwise the default.
        var userByCommand = new Dictionary<ReaderCommand, string>();
        if (overrides is not null)
        {
            foreach (var (chordText, commandText) in overrides)
            {
                if (ReaderCommandParser.TryParse(commandText, out var command))
                {
                    userByCommand[command] = chordText;
                }
            }
        }

        KeyBindings.Clear();
        foreach (var command in EnumerateBindableCommands())
        {
            var chord = userByCommand.TryGetValue(command, out var u) ? u
                : _defaultChordsByCommand.TryGetValue(command, out var d) ? d
                : string.Empty;
            KeyBindings.Add(new KeyBindingRow(command, HumanizeCommand(command), chord));
        }
    }

    /// <summary>
    /// Recompute the bindings list when the user picks a different layout —
    /// the defaults differ between desktop and laptop. User overrides are
    /// preserved per-command.
    /// </summary>
    public void RebuildBindingsForLayout()
    {
        var preserved = KeyBindings.ToDictionary(r => r.Command, r => r.Chord);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cmd, chord) in preserved)
        {
            if (!string.IsNullOrEmpty(chord))
            {
                dict[chord] = ReaderCommandParser.Format(cmd);
            }
        }
        BuildKeyBindings(dict, ResolveLayout(_layout));
    }

    private static KeyboardLayout ResolveLayout(string? raw)
        => string.Equals(raw, "laptop", StringComparison.OrdinalIgnoreCase)
            ? KeyboardLayout.Laptop
            : KeyboardLayout.Desktop;

    private static IEnumerable<ReaderCommand> EnumerateBindableCommands()
    {
        foreach (var v in Enum.GetValues<ReaderCommand>())
        {
            if (v != ReaderCommand.None)
            {
                yield return v;
            }
        }
    }

    private static string HumanizeCommand(ReaderCommand command) => command switch
    {
        ReaderCommand.StopSpeech => "Stop speech",
        ReaderCommand.SayAll => "Say all",
        ReaderCommand.SayAllFromCursor => "Say all from cursor",
        ReaderCommand.ReadCharacter => "Read current character",
        ReaderCommand.ReadNextCharacter => "Next character",
        ReaderCommand.ReadPreviousCharacter => "Previous character",
        ReaderCommand.ReadWord => "Read current word",
        ReaderCommand.ReadNextWord => "Next word",
        ReaderCommand.ReadPreviousWord => "Previous word",
        ReaderCommand.ReadLine => "Read current line",
        ReaderCommand.ReadNextLine => "Next line",
        ReaderCommand.ReadPreviousLine => "Previous line",
        ReaderCommand.ReviewMoveToFocus => "Review at focus",
        ReaderCommand.ReviewMoveToTop => "Review to top",
        ReaderCommand.ReviewMoveToBottom => "Review to bottom",
        ReaderCommand.ReportFocus => "Report focused control",
        ReaderCommand.ReportTitle => "Report window title",
        ReaderCommand.ReportTime => "Report time",
        ReaderCommand.ReportDate => "Report date",
        ReaderCommand.CyclePunctuationLevel => "Cycle punctuation level",
        ReaderCommand.ToggleKeyboardHelp => "Toggle keyboard help",
        ReaderCommand.ToggleEnabled => "Toggle screen reader",
        ReaderCommand.OpenSettings => "Open settings",
        ReaderCommand.OpenDocumentation => "Open documentation",
        ReaderCommand.OpenExitDialog => "Open exit dialog",
        ReaderCommand.OpenSynthesizerDialog => "Open synthesizer dialog",
        _ => command.ToString(),
    };

    public string VoiceId
    {
        get => _voiceId;
        set => Set(ref _voiceId, value);
    }

    public float RatePercent
    {
        get => _ratePercent;
        set => Set(ref _ratePercent, Math.Clamp(value, 25f, 400f));
    }

    public float VolumeDelta
    {
        get => _volumeDelta;
        set => Set(ref _volumeDelta, Math.Clamp(value, -100f, 100f));
    }

    public float PitchDelta
    {
        get => _pitchDelta;
        set => Set(ref _pitchDelta, Math.Clamp(value, -12f, 12f));
    }

    public string Profile
    {
        get => _profile;
        set => Set(ref _profile, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => Set(ref _startWithWindows, value);
    }

    public string Layout
    {
        get => _layout;
        set
        {
            if (Set(ref _layout, value))
            {
                RebuildBindingsForLayout();
            }
        }
    }

    /// <summary>
    /// One toggle for every named key. When off, no key name is spoken under
    /// any circumstance — including "backspace".
    /// </summary>
    public bool SpeakCommandKeys
    {
        get => _speakCommandKeys;
        set => Set(ref _speakCommandKeys, value);
    }

    public bool SpeakCharacters
    {
        get => _speakCharacters;
        set => Set(ref _speakCharacters, value);
    }

    public bool SpeakWords
    {
        get => _speakWords;
        set => Set(ref _speakWords, value);
    }

    /// <summary>
    /// Separate from <see cref="SpeakCharacters"/> on purpose: deletion is
    /// destructive and cannot be verified any other way, so a user who finds
    /// per-character echo too chatty still needs to hear what vanished.
    /// </summary>
    public bool SpeakDeletedCharacters
    {
        get => _speakDeletedCharacters;
        set => Set(ref _speakDeletedCharacters, value);
    }

    /// <summary>Project the current view-model state back into a persistable <see cref="ReaderConfig"/>.</summary>
    public ReaderConfig ToConfig() => new()
    {
        Version = 1,
        General = new GeneralConfig
        {
            Profile = string.IsNullOrEmpty(_profile) ? "default" : _profile,
            StartWithWindows = _startWithWindows,
        },
        Speech = new SpeechConfig
        {
            Engine = "sapi5",
            VoiceId = string.IsNullOrEmpty(_voiceId) ? null : _voiceId,
            RatePercent = _ratePercent,
            VolumeDelta = _volumeDelta,
            PitchDelta = _pitchDelta,
        },
        Keyboard = new KeyboardConfig
        {
            Layout = _layout,
            SpeakCommandKeys = _speakCommandKeys,
            SpeakCharacters = _speakCharacters,
            SpeakWords = _speakWords,
            SpeakDeletedCharacters = _speakDeletedCharacters,
        },
        Input = new InputConfig
        {
            ReaderModifier = "both",
            KeyBindings = BuildKeyBindingOverrides(),
        },
    };

    /// <summary>
    /// Project the rows back into the chord→command override dictionary. Rows
    /// whose chord matches the layout default are omitted so the user's saved
    /// file stays small and "follows the defaults" if we change them later.
    /// </summary>
    private Dictionary<string, string> BuildKeyBindingOverrides()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in KeyBindings)
        {
            if (string.IsNullOrEmpty(row.Chord))
            {
                continue;
            }
            if (_defaultChordsByCommand.TryGetValue(row.Command, out var def)
                && string.Equals(def, row.Chord, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            dict[row.Chord] = ReaderCommandParser.Format(row.Command);
        }
        return dict;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
