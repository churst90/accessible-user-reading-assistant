using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aura.Platform.Windows.Speech.EspeakNg;

/// <summary>
/// P/Invoke surface for <c>libespeak-ng.dll</c>. Mirrors the C API in
/// <c>speak_lib.h</c> (eSpeak NG ≥ 1.50).
/// </summary>
/// <remarks>
/// <para>
/// The library is loaded by simple name (<c>libespeak-ng</c>); on Windows the
/// runtime resolves this against the standard search path, so any normal
/// install (<c>%ProgramFiles%\eSpeak NG\</c> on PATH, or the binary alongside
/// our exe) works without further configuration. <c>DllNotFoundException</c>
/// is the canonical signal that the user does not have eSpeak NG installed —
/// callers catch it at engine construction time and fall back to SAPI.
/// </para>
/// <para>
/// All functions return <c>0</c> on success; non-zero is an error code from
/// <c>espeak_ERROR</c>. The <c>espeak_Synth</c> call blocks (in
/// <see cref="AudioOutput.SynchronousPlayback"/> mode) until audio finishes
/// or <see cref="espeak_Cancel"/> interrupts. Cancellation from another
/// thread is documented-safe.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class EspeakNgInterop
{
    public const string LibraryName = "libespeak-ng";

    /// <summary>How <c>espeak_Initialize</c> should route audio.</summary>
    public enum AudioOutput
    {
        /// <summary>Asynchronous playback to default audio device. Returns immediately.</summary>
        Playback = 0,
        /// <summary>No audio output. Caller retrieves PCM via the synth callback.</summary>
        Retrieval = 1,
        /// <summary>Synchronous, no audio output — synth callback fires inline.</summary>
        Synchronous = 2,
        /// <summary>Synchronous playback. <c>espeak_Synth</c> blocks until audio finishes.</summary>
        SynchronousPlayback = 3,
    }

    /// <summary>Flags passed to <c>espeak_Synth</c>'s <c>flags</c> argument.</summary>
    [Flags]
    public enum SpeakFlags : uint
    {
        CharsAuto = 0,
        CharsUtf8 = 1,
        CharsByte = 2,
        CharsUtf16 = 3,
        SsmlAware = 0x10,
        Phonemes = 0x100,
        EndPause = 0x1000,
    }

    /// <summary>Voice/synth parameters tunable via <c>espeak_SetParameter</c>.</summary>
    public enum Parameter
    {
        Rate = 1,        // wpm; 80..450, default 175
        Volume = 2,      // 0..200, default 100
        Pitch = 3,       // 0..100, default 50
        Range = 4,       // 0..100, default 50
        Punctuation = 5,
        Capitals = 6,    // 0=none, 1=sound, 2="cap" prefix, ≥3=raise pitch by N semitones
        Wordgap = 7,     // pause between words in 10ms units
    }

    /// <summary>Marshal-friendly mirror of <c>espeak_VOICE</c>. All string fields are 0-terminated UTF-8.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VoiceInfoNative
    {
        public IntPtr Name;
        public IntPtr Languages;
        public IntPtr Identifier;
        public byte Gender;     // 0=none, 1=male, 2=female
        public byte Age;
        public byte Variant;
        public byte XX1;
        public int Score;
        public IntPtr Spare;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern int espeak_Initialize(
        AudioOutput output,
        int bufLength,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? path,
        int options);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int espeak_Terminate();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int espeak_Cancel();

    /// <summary>
    /// Synth callback invoked from <c>espeak_Synth</c> while it is producing
    /// PCM samples. <paramref name="wav"/> points to a buffer of <c>numsamples</c>
    /// 16-bit signed little-endian mono samples at the rate returned by
    /// <c>espeak_Initialize</c>. Returning <c>0</c> continues synthesis;
    /// returning <c>1</c> aborts.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SynthCallback(IntPtr wav, int numsamples, IntPtr events);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int espeak_SetSynthCallback(SynthCallback callback);

    /// <summary>True (non-zero) if a Synth call is currently producing audio.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int espeak_IsPlaying();

    /// <summary>Block until any in-flight synth completes.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int espeak_Synchronize();

    /// <summary>
    /// Synthesize <paramref name="text"/>. With <see cref="AudioOutput.SynchronousPlayback"/>
    /// this blocks until the audio finishes or <see cref="espeak_Cancel"/> fires.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int espeak_Synth(
        IntPtr text,
        UIntPtr size,
        uint positionStart,
        int positionType,
        uint endPosition,
        uint flags,
        IntPtr uniqueIdentifier,
        IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int espeak_SetParameter(Parameter parameter, int value, int relative);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int espeak_GetParameter(Parameter parameter, int current);

    /// <summary>Select a voice by name or alias (e.g. <c>"en"</c>, <c>"en-us"</c>, <c>"english_rp"</c>).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern int espeak_SetVoiceByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    /// <summary>
    /// Returns a pointer to a null-terminated array of <c>espeak_VOICE*</c>.
    /// Pass <c>IntPtr.Zero</c> as the spec to enumerate all voices.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr espeak_ListVoices(IntPtr voiceSpec);
}
