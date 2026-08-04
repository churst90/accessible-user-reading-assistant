using System.Globalization;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Diagnostics;
using Serilog;
using AbsVoiceInfo = Aura.Abstractions.Speech.VoiceInfo;
using AbsVoiceGender = Aura.Abstractions.Speech.VoiceGender;
using SapiVoiceInfo = System.Speech.Synthesis.VoiceInfo;
using SapiVoiceGender = System.Speech.Synthesis.VoiceGender;

namespace Aura.Platform.Windows.Speech;

/// <summary>
/// <see cref="ISpeechEngine"/> implementation over the SAPI5-backed
/// <see cref="SpeechSynthesizer"/>. Single-voice playback at a time;
/// the queue layer above handles arbitration.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Sapi5Engine : ISpeechEngine
{
    private readonly SpeechSynthesizer _synth;
    private readonly object _gate = new();
    private readonly ILogger _log;
    private TaskCompletionSource? _currentTcs;
    private Prompt? _currentPrompt;
    private CancellationTokenRegistration _currentRegistration;
    private bool _disposed;

    public Sapi5Engine()
    {
        _synth = new SpeechSynthesizer();
        _synth.SetOutputToDefaultAudioDevice();
        _synth.SpeakCompleted += OnSpeakCompleted;
        _log = LoggerFactory.ForComponent("Speech.Sapi5");

        var voices = new List<AbsVoiceInfo>();
#pragma warning disable CA1304 // GetInstalledVoices() returns all SAPI voices regardless of locale.
        var installedVoices = _synth.GetInstalledVoices();
#pragma warning restore CA1304
        foreach (var installed in installedVoices)
        {
            if (!installed.Enabled)
            {
                continue;
            }
            voices.Add(MapVoice(installed.VoiceInfo));
        }
        Voices = voices;
        DefaultVoiceId = _synth.Voice?.Name;

        _log.Information("SAPI5 engine ready with {Count} voices, default '{Voice}'", voices.Count, DefaultVoiceId);
    }

    public string Id => "sapi5";

    public IReadOnlyList<AbsVoiceInfo> Voices { get; }

    public string? DefaultVoiceId { get; set; }

    /// <summary>
    /// Default prosody applied to utterances whose <see cref="ProsodyHint"/> is
    /// <see cref="ProsodyHint.Default"/>. Surfaces the user's "rate / volume / pitch"
    /// from the config layer without making every rule restate them.
    /// </summary>
    public ProsodyHint DefaultProsody { get; set; } = ProsodyHint.Default;


    /// <inheritdoc />
    // Contract surface. Nothing raises it yet: emitting markers needs
    // MarkerPart to reach the engine as an SSML bookmark / index, which is the
    // say-all-resume work. Capabilities deliberately does not advertise
    // OutputCapabilities.Marker, so nothing depends on it in the meantime.
#pragma warning disable CS0067
    public event Action<int>? MarkerReached;
#pragma warning restore CS0067

    public ValueTask SpeakAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        TaskCompletionSource? previous;
        TaskCompletionSource newTcs;
        lock (_gate)
        {
            previous = _currentTcs;
            newTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _currentTcs = newTcs;
            _currentPrompt = null; // cleared until SpeakAsync returns the new Prompt
        }
        previous?.TrySetCanceled(CancellationToken.None);

        try
        {
            var text = utterance.PlainText();
            ApplyVoice(utterance.VoiceId);
            // Compose the per-utterance prosody on top of the user's defaults.
            // Rate is multiplicative (a "slow this down to 80%" rule on top of
            // a 200% user rate becomes 160%); pitch and volume are additive
            // deltas. Previously this used IsDefault as an either-or switch,
            // which meant ANY rule emitting a default-valued ProsodyHint
            // (rate 100, pitch 0, volume 0) silently overrode the user's
            // default rate — that's the "speech rate slider does nothing"
            // bug.
            var effective = ComposeProsody(utterance.Prosody, DefaultProsody);
            ApplyProsody(effective);
            _currentRegistration.Dispose();
            _currentRegistration = cancellationToken.Register(static state => ((Sapi5Engine)state!).CancelInternal(),
                this, useSynchronizationContext: false);
            // Pitch is per-utterance, applied via SSML markup (SAPI doesn't
            // expose a Pitch property — only Rate and Volume). When pitch
            // delta is zero, send plain text to skip SSML parse cost.
            var prompt = effective.PitchDelta != 0f
                ? _synth.SpeakSsmlAsync(BuildPitchSsml(text, effective.PitchDelta))
                : _synth.SpeakAsync(text);
            lock (_gate)
            {
                if (ReferenceEquals(_currentTcs, newTcs))
                {
                    _currentPrompt = prompt;
                }
            }
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            lock (_gate)
            {
                _currentTcs?.TrySetException(ex);
                _currentTcs = null;
                _currentPrompt = null;
            }
            throw;
        }

        return new ValueTask(newTcs.Task);
    }

    public ValueTask CancelAsync()
    {
        CancelInternal();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }
            _disposed = true;
        }
        try
        {
            _synth.SpeakAsyncCancelAll();
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception while cancelling on dispose");
        }
        _synth.SpeakCompleted -= OnSpeakCompleted;
        _synth.Dispose();
        _currentRegistration.Dispose();
        return ValueTask.CompletedTask;
    }

    private void CancelInternal()
    {
        try
        {
            _synth.SpeakAsyncCancelAll();
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "SpeakAsyncCancelAll threw");
        }
    }

    private void ApplyVoice(string? requestedVoiceId)
    {
        var name = requestedVoiceId ?? DefaultVoiceId;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }
        try
        {
            if (!string.Equals(_synth.Voice?.Name, name, StringComparison.Ordinal))
            {
                _synth.SelectVoice(name);
            }
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "could not select voice '{Voice}'", name);
        }
    }

    /// <summary>
    /// Combine a per-utterance prosody hint with the user's defaults. Rate is
    /// multiplicative (rule rate × default rate / 100), pitch and volume are
    /// additive deltas.
    /// </summary>
    /// <remarks>Internal so tests can assert composition semantics directly without spinning up SAPI.</remarks>
    internal static ProsodyHint ComposeProsody(ProsodyHint utterance, ProsodyHint defaults)
    {
        // RatePercent uses 100 as neutral; multiply ratios.
        var rate = utterance.RatePercent * defaults.RatePercent / 100f;
        return new ProsodyHint(
            PitchDelta: utterance.PitchDelta + defaults.PitchDelta,
            RatePercent: rate,
            VolumeDelta: utterance.VolumeDelta + defaults.VolumeDelta);
    }

    /// <summary>
    /// Wrap <paramref name="text"/> in SAPI's SSML <c>&lt;prosody pitch&gt;</c>
    /// markup. Pitch is expressed as a percent change relative to the voice's
    /// default; we translate semitones to percent at roughly +5%/semitone
    /// (SAPI accepts -50% to +50%). XML-escape user text so a literal
    /// <c>&lt;/prosody&gt;</c> in the input cannot break out of the wrapper.
    /// </summary>
    private static string BuildPitchSsml(string text, float pitchDelta)
    {
        var pct = (int)Math.Clamp(Math.Round(pitchDelta * 5f), -50f, 50f);
        var sign = pct >= 0 ? "+" : "";
        var escaped = System.Security.SecurityElement.Escape(text) ?? string.Empty;
        return "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>"
             + $"<prosody pitch='{sign}{pct}%'>{escaped}</prosody></speak>";
    }

    private void ApplyProsody(ProsodyHint prosody)
    {
        // SAPI Rate is an integer in [-10, +10] where 0 is the voice's
        // neutral pace, and the perceived speed change per step is roughly
        // 10 % compounding (so SAPI 7 ≈ 1.10⁷ ≈ 1.95× speed).
        //
        // RatePercent is the user-facing speed multiplier in percent (100
        // = neutral, 200 = "twice as fast"). A linear 25 %-per-step mapping
        // saturated SAPI early — 210 % only reached SAPI 4 (~1.46× speed),
        // which is why users set "210 %" and still heard sluggish speech.
        // Use a log mapping so the perceived speed actually matches the
        // dial:  N = log(percent/100) / log(1.10).
        var ratio = Math.Clamp(prosody.RatePercent, 25f, 400f) / 100f;
        var rate = (int)Math.Clamp(Math.Round(Math.Log(ratio) / Math.Log(1.10)), -10.0, 10.0);
        _synth.Rate = rate;

        // Volume is 0..100, default 100.
        var volume = (int)Math.Clamp(100f + prosody.VolumeDelta, 0f, 100f);
        _synth.Volume = volume;
    }

    private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        TaskCompletionSource? tcs;
        lock (_gate)
        {
            // Two cases to handle:
            //
            // 1. _currentPrompt is set and matches e.Prompt → normal completion;
            //    resolve.
            // 2. _currentPrompt is null but _currentTcs is set → race window.
            //    SpeakAsync reserved the TCS but hasn't yet stored the prompt
            //    (the synth ran fast enough that completion fires before
            //    `_currentPrompt = prompt`). The drain loop is serial, so
            //    only one prompt is ever in flight per engine at this point —
            //    accept the completion and resolve. Without this branch the
            //    TCS hangs forever and the drain loop deadlocks, which makes
            //    every cancel path appear broken even though SAPI itself is
            //    cancelling correctly.
            //
            // 3. _currentPrompt is set but doesn't match e.Prompt → a stale
            //    event for a prior prompt; ignore so the current TCS isn't
            //    wrongly resolved.
            if (_currentTcs is null)
            {
                return;
            }
            if (_currentPrompt is not null && !ReferenceEquals(_currentPrompt, e.Prompt))
            {
                return;
            }
            tcs = _currentTcs;
            _currentTcs = null;
            _currentPrompt = null;
        }
        _currentRegistration.Dispose();
        if (tcs is null)
        {
            return;
        }
        if (e.Cancelled)
        {
            tcs.TrySetCanceled(CancellationToken.None);
        }
        else if (e.Error is not null)
        {
            tcs.TrySetException(e.Error);
        }
        else
        {
            tcs.TrySetResult();
        }
    }

    private static AbsVoiceInfo MapVoice(SapiVoiceInfo info)
    {
        var gender = info.Gender switch
        {
            SapiVoiceGender.Male => AbsVoiceGender.Male,
            SapiVoiceGender.Female => AbsVoiceGender.Female,
            SapiVoiceGender.Neutral => AbsVoiceGender.Neutral,
            _ => AbsVoiceGender.Unknown,
        };
        var culture = info.Culture ?? CultureInfo.InvariantCulture;
        return new AbsVoiceInfo(info.Name, info.Description ?? info.Name, culture, gender, IsNeural: false);
    }

    private static bool IsCritical(Exception ex)
        => ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;
}
