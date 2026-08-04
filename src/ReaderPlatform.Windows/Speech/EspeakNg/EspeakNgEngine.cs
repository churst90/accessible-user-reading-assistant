using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.Wave;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Diagnostics;
using Serilog;
using Engine = Aura.Platform.Windows.Speech.EspeakNg.EspeakNgInterop;

namespace Aura.Platform.Windows.Speech.EspeakNg;

/// <summary>
/// <see cref="ISpeechEngine"/> over <c>libespeak-ng.dll</c> with
/// caller-managed audio playback for instant cancellation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why we don't use SynchronousPlayback.</b> eSpeak's built-in playback
/// modes buffer ~100–500 ms of audio in the OS audio stack and
/// <c>espeak_Cancel</c> only stops <em>synthesis</em>, not the already-queued
/// playback. The result is that pressing arrows fast piled up announcements
/// — old audio drained while new utterances queued behind it — exactly the
/// opposite of what a screen reader needs. Instead we run eSpeak in
/// <see cref="Engine.AudioOutput.Synchronous"/> mode (no audio output)
/// and pipe its PCM samples through NAudio's <see cref="BufferedWaveProvider"/>
/// to a <see cref="WaveOutEvent"/>. Cancel calls <c>espeak_Cancel</c> AND
/// flushes the buffer — the audio device immediately starts playing silence
/// and the next utterance feeds in cleanly with no stale samples.
/// </para>
/// <para>
/// <b>Threading.</b> eSpeak's API is not thread-safe; the synth callback
/// fires on whatever thread is in <c>espeak_Synth</c>. We serialize <see cref="SpeakAsync"/>
/// via the queue's drain loop (one utterance at a time). The synth callback
/// pushes bytes into <c>BufferedWaveProvider</c> which is itself thread-safe.
/// </para>
/// <para>
/// Construction throws <see cref="DllNotFoundException"/> when the user has
/// not installed eSpeak NG. The host catches that and falls back to SAPI 5.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class EspeakNgEngine : ISpeechEngine
{
    private readonly object _gate = new();
    private readonly ILogger _log;
    private readonly int _sampleRate;
    private readonly WaveFormat _waveFormat;
    private readonly BufferedWaveProvider _buffer;
    private readonly WaveOutEvent _output;
    // Hold the delegate as a field so the GC doesn't collect it while
    // unmanaged code holds a function pointer to it.
    private readonly Engine.SynthCallback _callback;
    private volatile bool _cancelled;
    private bool _disposed;

    public EspeakNgEngine(string? dataPath = null)
    {
        _log = LoggerFactory.ForComponent("Speech.EspeakNg");
        _sampleRate = Engine.espeak_Initialize(Engine.AudioOutput.Synchronous, 0, dataPath, 0);
        if (_sampleRate < 0)
        {
            throw new InvalidOperationException(
                $"espeak_Initialize returned {_sampleRate}. Library is loaded but data files were not found.");
        }
        _waveFormat = new WaveFormat(_sampleRate, bits: 16, channels: 1);
        _buffer = new BufferedWaveProvider(_waveFormat)
        {
            DiscardOnBufferOverflow = false,
            // Hold up to ~10 s of audio. Long enough for a Say-All paragraph,
            // small enough to keep memory bounded if synth outpaces playback.
            BufferDuration = TimeSpan.FromSeconds(10),
        };
        // 60 ms playback latency: short enough that "instant cancel" feels
        // instant, long enough not to glitch on a busy CPU. The audible
        // cancel lag is bounded by this — at the moment we ClearBuffer the
        // device still has ~60 ms of already-handed-off samples in flight.
        _output = new WaveOutEvent { DesiredLatency = 60, NumberOfBuffers = 3 };
        _output.Init(_buffer);
        _output.Play();

        _callback = OnSynthCallback;
        _ = Engine.espeak_SetSynthCallback(_callback);

        Voices = LoadVoices();
        _log.Information("eSpeak NG ready: {Count} voices, sample rate {Rate} Hz", Voices.Count, _sampleRate);
    }

    public string Id => "espeak-ng";

    public IReadOnlyList<VoiceInfo> Voices { get; }

    public string? DefaultVoiceId { get; set; }

    /// <summary>
    /// Default prosody applied when an utterance's <see cref="ProsodyHint"/> is
    /// itself <see cref="ProsodyHint.Default"/>. Surfaces the user's
    /// "rate / volume / pitch" config without making every rule restate them.
    /// </summary>
    public ProsodyHint DefaultProsody { get; set; } = ProsodyHint.Default;

    /// <summary>
    /// Synth callback. Receives 16-bit signed mono PCM samples from eSpeak
    /// during synthesis and pushes them into the playback buffer. Returning
    /// 1 tells eSpeak to abort the current synth — that's how we honour
    /// cancellation without waiting for the full utterance to render.
    /// </summary>
    private int OnSynthCallback(IntPtr wav, int numsamples, IntPtr events)
    {
        if (_cancelled || _disposed)
        {
            return 1;
        }
        if (numsamples > 0 && wav != IntPtr.Zero)
        {
            try
            {
                // 16-bit PCM: 2 bytes per sample.
                var byteCount = numsamples * 2;
                var managed = new byte[byteCount];
                Marshal.Copy(wav, managed, 0, byteCount);
                _buffer.AddSamples(managed, 0, byteCount);
            }
            catch (Exception ex) when (!IsCritical(ex))
            {
                _log.Warning(ex, "synth callback dropped a chunk");
                return 1;
            }
        }
        return 0;
    }


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

        return new ValueTask(SpeakInternalAsync(utterance, cancellationToken));
    }

    private async Task SpeakInternalAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        // Clear the cancellation flag so the next callback iteration accepts
        // samples again. Any leftover audio in the buffer from a preempted
        // utterance gets dropped here — this is the "instant silence on
        // preempt" guarantee.
        _cancelled = false;
        _buffer.ClearBuffer();

        var effective = ComposeProsody(utterance.Prosody, DefaultProsody);
        ApplyVoice(utterance.VoiceId);
        ApplyProsody(effective);

        // Cancel from another thread interrupts both synth and audio playback.
        await using var ctsReg = cancellationToken.Register(static state =>
        {
            ((EspeakNgEngine)state!).CancelInternal();
        }, this).ConfigureAwait(false);

        try
        {
            await Task.Run(() => SynthBlocking(utterance.PlainText()), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected — preempted by a higher-priority utterance.
        }
    }

    private void SynthBlocking(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            // Serialize Synth calls — eSpeak's API is not re-entrant. Drain
            // loop is sequential anyway; the lock is defence in depth.
            lock (_gate)
            {
                _ = Engine.espeak_Synth(
                    handle.AddrOfPinnedObject(),
                    (UIntPtr)bytes.Length,
                    0, 0, 0,
                    (uint)(Engine.SpeakFlags.CharsUtf8 | Engine.SpeakFlags.EndPause),
                    IntPtr.Zero, IntPtr.Zero);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    public ValueTask CancelAsync()
    {
        CancelInternal();
        return ValueTask.CompletedTask;
    }

    private void CancelInternal()
    {
        _cancelled = true;
        try { _ = Engine.espeak_Cancel(); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "espeak_Cancel threw"); }
        // Drop everything queued for playback so the user hears silence
        // instantly, not the trail-out of an utterance we're cancelling.
        try { _buffer.ClearBuffer(); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "buffer clear threw"); }
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
        _cancelled = true;
        try { _ = Engine.espeak_Cancel(); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "cancel on dispose threw"); }
        try { _output.Stop(); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "wave-out stop threw"); }
        try { _output.Dispose(); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "wave-out dispose threw"); }
        try { _ = Engine.espeak_Terminate(); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "terminate threw"); }
        return ValueTask.CompletedTask;
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
            _ = Engine.espeak_SetVoiceByName(name);
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "could not select voice '{Voice}'", name);
        }
    }

    private static void ApplyProsody(ProsodyHint prosody)
    {
        // eSpeak rate is words-per-minute; default 175, range [80..450].
        // RatePercent = 100 → 175 wpm. Linear scale matches the user's
        // expectation that "200%" is twice as fast.
        var rateWpm = (int)Math.Clamp(175f * Math.Clamp(prosody.RatePercent, 25f, 400f) / 100f, 80f, 450f);
        _ = Engine.espeak_SetParameter(Engine.Parameter.Rate, rateWpm, 0);

        // Volume: eSpeak 0..200, default 100. VolumeDelta is +/-100 percent
        // points, so add to 100 directly.
        var vol = (int)Math.Clamp(100f + prosody.VolumeDelta, 0f, 200f);
        _ = Engine.espeak_SetParameter(Engine.Parameter.Volume, vol, 0);

        // Pitch: eSpeak 0..100, default 50. PitchDelta is in semitones (-12..+12);
        // map roughly 1 semitone ≈ 4 pitch units (covers a +/-3 octave range).
        var pitch = (int)Math.Clamp(50f + prosody.PitchDelta * 4f, 0f, 100f);
        _ = Engine.espeak_SetParameter(Engine.Parameter.Pitch, pitch, 0);
    }

    private static ProsodyHint ComposeProsody(ProsodyHint utterance, ProsodyHint defaults) => new(
        PitchDelta: utterance.PitchDelta + defaults.PitchDelta,
        RatePercent: utterance.RatePercent * defaults.RatePercent / 100f,
        VolumeDelta: utterance.VolumeDelta + defaults.VolumeDelta);

    private static List<VoiceInfo> LoadVoices()
    {
        var voices = new List<VoiceInfo>();
        var arrayPtr = Engine.espeak_ListVoices(IntPtr.Zero);
        if (arrayPtr == IntPtr.Zero)
        {
            return voices;
        }
        // Walk the null-terminated array of espeak_VOICE pointers.
        var entry = arrayPtr;
        while (true)
        {
            var voicePtr = Marshal.ReadIntPtr(entry);
            if (voicePtr == IntPtr.Zero)
            {
                break;
            }
            var native = Marshal.PtrToStructure<Engine.VoiceInfoNative>(voicePtr);
            var v = TranslateVoice(native);
            if (v is not null)
            {
                voices.Add(v);
            }
            entry += IntPtr.Size;
        }
        return voices;
    }

    private static VoiceInfo? TranslateVoice(Engine.VoiceInfoNative native)
    {
        var identifier = MarshalUtf8(native.Identifier);
        if (string.IsNullOrEmpty(identifier))
        {
            return null;
        }
        var name = MarshalUtf8(native.Name) ?? identifier;
        var lang = ReadFirstLanguage(native.Languages);
        var culture = TryGetCulture(lang);
        var gender = native.Gender switch
        {
            1 => VoiceGender.Male,
            2 => VoiceGender.Female,
            _ => VoiceGender.Unknown,
        };
        return new VoiceInfo(identifier, name, culture, gender, IsNeural: false);
    }

    private static string? MarshalUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }
        var len = 0;
        while (Marshal.ReadByte(ptr, len) != 0)
        {
            len++;
            if (len > 4096)
            {
                break;
            }
        }
        if (len == 0)
        {
            return null;
        }
        var buffer = new byte[len];
        Marshal.Copy(ptr, buffer, 0, len);
        return System.Text.Encoding.UTF8.GetString(buffer);
    }

    private static string? ReadFirstLanguage(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }
        // First byte is priority; skip it and read the null-terminated tag.
        var tagPtr = ptr + 1;
        return MarshalUtf8(tagPtr);
    }

    private static CultureInfo TryGetCulture(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return CultureInfo.InvariantCulture;
        }
        try
        {
            return CultureInfo.GetCultureInfo(tag.Replace('_', '-'));
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static bool IsCritical(Exception ex)
        => ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;
}
