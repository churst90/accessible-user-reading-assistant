using Aura.Abstractions.Speech;

namespace Aura.Abstractions.Output;

/// <summary>
/// One instruction in an <see cref="Utterance"/>.
/// </summary>
/// <remarks>
/// <para>
/// Speech is a <em>sequence</em>, not a string. Every part below exists because
/// a flat string could not express something a user needed: a quotation in
/// another language read in that language's voice, a capital raised in pitch
/// rather than announced, an earcon inside an announcement rather than queued
/// behind it, a pause that is not a comma, and a position marker so say-all
/// knows where it has got to.
/// </para>
/// <para>
/// An engine that cannot honour a part <b>ignores it</b>. Nothing here may
/// throw: an unsupported part must degrade to silence for that part, never to
/// a lost announcement.
/// </para>
/// </remarks>
public abstract record OutputPart;

/// <summary>Speak these words.</summary>
public sealed record TextPart(string Text) : OutputPart;

/// <summary>Spell these characters out rather than pronouncing them as a word.</summary>
public sealed record SpellPart(string Text) : OutputPart;

/// <summary>
/// Switch language for what follows. <c>null</c> returns to the default.
/// </summary>
/// <remarks>
/// The reader's own words — a role, a state, a position — are always in the
/// reader's language, never the document's. A renderer must return to default
/// before emitting them or "button" gets read with a French accent inside a
/// French page.
/// </remarks>
public sealed record LanguagePart(string? BcpTag) : OutputPart;

/// <summary>Apply a prosody delta to what follows, until the matching pop.</summary>
/// <remarks>
/// A stack rather than NVDA's is-default flag, because an utterance that is
/// split by a higher-priority interruption has to re-establish whatever was in
/// effect when it resumes. With a stack that is a copy; without one it is a
/// bookkeeping class. An unbalanced sequence is a bug a renderer may assert on.
/// </remarks>
public sealed record ProsodyPush(ProsodyHint Delta) : OutputPart;

/// <summary>Undo the most recent <see cref="ProsodyPush"/>.</summary>
public sealed record ProsodyPop : OutputPart;

/// <summary>Pause. Not a comma — punctuation changes the words, this does not.</summary>
public sealed record BreakPart(TimeSpan Duration) : OutputPart;

/// <summary>Play an earcon here, inside the utterance.</summary>
public sealed record CuePart(string CueId) : OutputPart;

/// <summary>
/// A position marker. The engine raises <see cref="ISpeechEngine.MarkerReached"/>
/// as synthesis passes it, which is how say-all resumes at the right word and
/// how braille follows speech.
/// </summary>
public sealed record MarkerPart(int Id) : OutputPart;

/// <summary>
/// End the utterance here and start a new one. Lets a long document start
/// speaking before all of it has been composed.
/// </summary>
public sealed record UtteranceBoundary : OutputPart;
