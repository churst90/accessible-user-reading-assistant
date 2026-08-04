# F1 — The output model

**Status:** specified, not built. Build first.
**Depends on:** nothing.
**Blocks:** F5a (golden transcripts), braille (4g), audio themes (4b), say-all
resume, per-language voices, and the permanent fix for the speech-staleness
bug class.

---

## Why

`SpeechUtterance` is:

```csharp
public sealed record SpeechUtterance(
    string Text, ProsodyHint Prosody, string? VoiceId,
    SpeechPriority Priority, string? CancelGroup, IReadOnlyList<string> RuleTrace);
```

One string, one prosody, one voice, for the whole announcement. Six things a
screen reader has to do cannot be said in that shape:

| Needed | Why a flat string cannot |
|---|---|
| A French quotation inside an English line | The voice must change part-way through and change back |
| Capitals raised in pitch | A prosody *span*, not a setting for the utterance |
| Say-all that resumes where it stopped | Needs a position marker the engine reports as it passes it |
| An earcon *inside* an announcement | "button" + ding + "Save", not ding queued behind the sentence |
| A pause that is not a comma | Punctuation changes the text; a break does not |
| Dropping an announcement that has gone stale | Needs a predicate the queue can evaluate at speak time |

That last row is the expensive one, and it is worth stating separately because
it has already cost four rounds of bugs.

### The staleness argument, stated properly

Every AURA speech bug of the last month is the same bug: *is this announcement
still wanted?*, answered with timing. Cancel on keypress; exclude arrows;
un-exclude arrows; make the cancel synchronous so it cannot race; add a
duplicate-text suppression window; remove it because blank lines went silent.

NVDA answers the same question with **state**. A focus announcement carries a
`FocusLossCancellableSpeechCommand` holding the object it describes
(`source/eventHandler.py:187`). The speech manager evaluates it *at the moment
the utterance would be spoken*. The predicate is: this object is still the
focus, OR it never had focus, OR it is an ancestor of the current focus, OR it
is the foreground object, OR it is a menu item of the current focus. On each new
focus event — *after* focus state has been updated —
`removeCancelledSpeechCommands()` sweeps the queue.

A stale announcement evaporates. A valid one survives. There is no timing, so
there is no race to lose. Cancel-on-input cannot distinguish those two cases,
which is exactly why it produced both symptoms: speech running an item behind
when it cancelled too little, and silence on backspace when it cancelled too
much.

**This cannot be added to a string.** It is why the staleness fix is part of F1
rather than a seventh attempt at tuning `Program.cs`.

### The braille argument

NVDA has two independent renderers: `TextInfo.getControlFieldSpeech` and
`TextInfo.getControlFieldBraille`. Both decide what a control's presentation is.
They disagree in small ways, permanently, because there is no single definition
to reconcile them against. AURA has no braille yet, so it still has the choice.
Building the speech path first and bolting braille on later *is* making the
wrong choice — it just defers noticing.

---

## The contract

Two layers. `Presentation` is what the rule engine produces — *what should be
conveyed*. `Utterance` is what a speech renderer turns that into.

### Layer 1 — `Presentation`

`src/Reader.Abstractions/Output/Presentation.cs`

```csharp
public sealed record Presentation(
    IReadOnlyList<PresentationSegment> Segments,
    SpeechReason Reason,
    string? Subject,                    // node id; what OutputArbiter keys on
    SpeechPriority Priority,
    string? CancelGroup,
    IValidityPredicate? Validity,       // see below
    IReadOnlyList<string> RuleTrace)
{
    public bool IsBlank => Segments.All(s => s.IsBlank);
}

public sealed record PresentationSegment(
    string Text,
    SegmentKind Kind,
    AccessibleRole? Role = null,
    IReadOnlyDictionary<string, object?>? Attributes = null,
    ITextRange? Source = null)          // braille cursor routing, say-all tracking
{
    public bool IsBlank => Kind is not SegmentKind.Cue && Blank.Is(Text);
}

public enum SegmentKind
{
    Content,      // the text itself — a line, a word, a character
    Name,         // the control's accessible name
    Role,         // "button", "list item"
    Value,        // the control's value
    State,        // "checked", "expanded", "read only"
    Position,     // "3 of 10", "level 2"
    Structure,    // "list", "out of list", "heading level 2"
    Indent,       // "12 spaces" or a tone
    Cue,          // an earcon; Text is the cue id, not words
    Hint,         // "press space to activate" — suppressible as a class
}
```

`SegmentKind` exists so that verbosity is a property of the *system* rather
than of each rule. "Never announce hints", "announce position only in lists",
"show state in braille but do not speak it" are all filters over kinds. In NVDA
these decisions are spread through `speech.py` as `formatConfig` checks at every
call site; here they are one filter over a list.

### The blank rule, taken from NVDA and worth taking exactly

`src/Reader.Abstractions/Output/Blank.cs`

```csharp
public static class Blank
{
    // NVDA's BLANK_CHUNK_CHARS. The non-breaking space matters: a line of
    // U+00A0 looks empty and is not, and web pages are full of them.
    private static readonly SearchValues<char> BlankChars =
        SearchValues.Create(" \n\r\0 ");

    public static bool Is(string? text) =>
        string.IsNullOrEmpty(text) || !text.AsSpan().ContainsAnyExcept(BlankChars);
}
```

And the rule that uses it, which is the answer to the blank-line bug:

> **"blank" is announced when, and only when, *nothing in the whole
> presentation* is non-blank — and never during say-all.**

That is `shouldConsiderTextInfoBlank` in `speech.py:~1930`, and it is why NVDA
says "list item" rather than "blank" on an empty bullet, and says nothing at all
when say-all crosses an empty line. The previous AURA attempt suppressed on
*content* and therefore could not tell "nothing moved" from "the next thing
reads the same". Blankness is a property of the composed presentation, not of a
string compared against the last string.

### Validity — the staleness predicate

`src/Reader.Abstractions/Output/IValidityPredicate.cs`

```csharp
/// <summary>Answers "is this announcement still worth speaking?" at speak time.</summary>
public interface IValidityPredicate
{
    bool IsStillValid();
}
```

Two implementations to start, both in `Reader.Core/Output/`:

- `FocusStillCurrent(NodeId subject)` — valid if the subject is the current
  focus, or an ancestor of it, or the foreground window, or a menu item of the
  current focus. (Copy NVDA's disjunction; each clause exists because of a
  specific reported bug, and the menu-item clause cites NVDA #12624.)
- `CaretStillAt(NodeId surface, int offset)` — valid if the caret has not moved
  since the announcement was composed.

`null` means unconditionally valid, which is right for user-requested speech —
if the user pressed a key to hear something, they get to hear it.

### Layer 2 — `Utterance`

`src/Reader.Abstractions/Output/OutputPart.cs`

```csharp
public abstract record OutputPart;

public sealed record TextPart(string Text)          : OutputPart;
public sealed record SpellPart(string Text)         : OutputPart;  // char mode on/off around it
public sealed record LanguagePart(string? BcpTag)   : OutputPart;  // null = back to default
public sealed record ProsodyPush(ProsodyHint Delta) : OutputPart;
public sealed record ProsodyPop()                   : OutputPart;
public sealed record BreakPart(TimeSpan Duration)   : OutputPart;
public sealed record CuePart(string CueId)          : OutputPart;  // earcon, inline
public sealed record MarkerPart(int Id)             : OutputPart;  // engine reports on passing
public sealed record UtteranceBoundary()            : OutputPart;  // chunk here

public sealed record Utterance(
    IReadOnlyList<OutputPart> Parts,
    SpeechPriority Priority,
    string? CancelGroup,
    IValidityPredicate? Validity,
    IReadOnlyList<string> RuleTrace);
```

**`ProsodyPush`/`ProsodyPop` rather than NVDA's `isDefault` flag.** NVDA tracks
parameter changes in a `ParamChangeTracker` so that when an utterance is split
— which happens whenever a higher-priority utterance interrupts — the parameter
commands still in effect can be re-emitted at the head of the resumed portion.
A push/pop stack makes that a `Stack<T>` copy instead of a bookkeeping class,
and makes an unbalanced sequence a bug the renderer can assert on.

### `ISpeechEngine` changes

```csharp
public interface ISpeechEngine : IAsyncDisposable
{
    ValueTask SpeakAsync(Utterance utterance, CancellationToken ct);
    ValueTask CancelAsync();
    IReadOnlyList<VoiceInfo> Voices { get; }

    /// Which parts this engine can honour. Unsupported parts degrade —
    /// never throw. An engine with no per-part language support ignores
    /// LanguagePart; the router may still pick a different engine per language.
    OutputCapabilities Capabilities { get; }

    /// Raised as synthesis passes a MarkerPart. Say-all and braille sync need this.
    event Action<int>? MarkerReached;
}

[Flags]
public enum OutputCapabilities
{
    None = 0, Language = 1, Prosody = 2, Break = 4,
    Marker = 8, Phoneme = 16, CharacterMode = 32,
}
```

`MarkerReached` is the one genuinely new engine obligation. SAPI 5 supplies it
through bookmarks; eSpeak NG through its index callback. Both already exist in
the respective APIs and neither is currently wired.

---

## How it will be implemented

New, in `Reader.Abstractions/Output/`:

| File | Contents |
|---|---|
| `Presentation.cs` | `Presentation`, `PresentationSegment`, `SegmentKind` |
| `Blank.cs` | the blank-character set and rule |
| `IValidityPredicate.cs` | the interface |
| `OutputPart.cs` | the part hierarchy |
| `Utterance.cs` | `Utterance`, `OutputCapabilities` |
| `IPresentationRenderer.cs` | `IPresentationRenderer<T> { T Render(Presentation p); }` |

New, in `Reader.Speech/Rendering/`:

| File | Contents |
|---|---|
| `SpeechRenderer.cs` | `Presentation` → `Utterance`. Owns the blank rule, symbol/punctuation processing, capital handling, and language-run splitting. |
| `TranscriptRenderer.cs` | `Presentation` → one deterministic line. **No engine, no timing, no Windows.** |
| `SegmentFilter.cs` | verbosity: which `SegmentKind`s survive, per reason, per config layer |

New, in `Reader.Core/Output/`: `FocusStillCurrent.cs`, `CaretStillAt.cs`.

Changed:

- `SpeechRuleEngine` / `SpeechTemplate` — emit segments instead of concatenating.
  The template tokens (`{name}`, `{role}`, `{value}`, `{position}`, `{level}`,
  `{posInSet}`) already map one-to-one onto `SegmentKind`, so this is closer to a
  re-typing than a rewrite.
- `OutputArbiter.Evaluate` — takes a `Presentation`. It already keys on subject
  and reason; both are now fields on the presentation rather than parameters.
- `SpeechQueue` — carries `Utterance`; gains `SweepInvalid()`, called by the host
  after focus state updates; drops any queued item whose `Validity` says no.
- `Sapi5Engine`, `EspeakNgEngine` — consume parts; raise `MarkerReached`.
- `Program.cs` — **delete** the cancel-on-every-keypress path. It is replaced by
  validity sweeping, and leaving both in place reintroduces the race that
  commit f1b38d4 fixed.

### The rendering rules that live in `SpeechRenderer`

Written down because they are currently scattered and will otherwise be
re-derived per call site, which is the NVDA failure mode this whole spec exists
to avoid:

1. Join segments with a separator that does not change pronunciation. NVDA uses
   two spaces (`CHUNK_SEPARATOR`) rather than a comma, deliberately, because a
   comma changes number reading in French and German.
2. Emit `LanguagePart` only when the language actually changes, and return to
   default before any `Structure`/`Role`/`State` segment — those are the
   reader's own words and belong in the reader's language, not the document's.
3. `Cue` segments become `CuePart` inline, never a separate queued sound.
4. Apply the blank rule last, over the whole composed presentation.
5. `Indent` is a segment so it can be rendered as a tone by one renderer and as
   words by another, and suppressed when unchanged from the previous line
   (NVDA's `indentationCache` — announce indentation on change, not on every
   line, or it is unbearable).

---

## Migration

Ordered so nothing is broken between steps.

1. **Add the types.** Pure addition to `Reader.Abstractions`. Nothing consumes
   them; nothing can regress.
2. **Write `TranscriptRenderer` and its tests.** Still consumes nothing real.
3. **Make the rule engine emit `Presentation`,** with a `Presentation.ToLegacy()`
   producing today's `SpeechUtterance` for every existing consumer. All 338 tests
   must still pass unchanged at this point — that is the checkpoint that says the
   segment decomposition is faithful.
4. **Switch `OutputArbiter` and `SpeechQueue` to `Presentation`/`Utterance`.**
   Delete `ToLegacy`.
5. **Add `Validity` and `SweepInvalid`.** Delete cancel-on-keypress. **This is
   the step that must be heard on the VM before anything is built on it** — it
   changes interrupt behaviour everywhere.
6. **Wire `MarkerReached`** in both engines. Nothing consumes it yet; say-all
   resume is the first consumer and belongs to a later phase.
7. **Retire `SpeechUtterance`** at the next contract version bump. Keep it as an
   `[Obsolete]` shim for one release so app modules do not break.

Steps 1–3 are additive and safe. Step 5 is the one with user-visible risk.

---

## Proof it landed

- Every existing speech test asserts on `TranscriptRenderer` output instead of
  `.Text`, and passes unchanged.
- A line containing a `lang`-attributed run renders a `LanguagePart` and returns
  to default before the role segment.
- An empty line inside a list item renders the list item and **not** "blank"; an
  empty line in a plain document renders "blank"; say-all across an empty line
  renders nothing.
- Arrowing up through three consecutive blank lines renders "blank" three times.
  *(This is the regression from commit c8a54e1, as a permanent test.)*
- A queued focus announcement whose subject has lost focus is swept and never
  spoken; one whose subject is an ancestor of the new focus survives.
- Backspace announces the deleted character. *(Roadmap 3.6 #6, which becomes
  nearly free: the deleted text is a `Content` segment captured before the
  keystroke.)*

---

## Open questions the implementing session must close

1. **Does `SegmentKind.Cue` belong here or in F-audio?** It is defined here so
   braille can ignore it and the transcript can render it as `[cue:name]`. But
   no cue exists until 4b. Risk of designing for an unbuilt consumer.
2. **Does `PresentationSegment.Source` (an `ITextRange`) create a lifetime
   problem?** Holding a UIA-backed range on a queued announcement means holding
   a COM object across threads — which is exactly what F4b is about. It may need
   to be a bookmark rather than a live range. **Resolve with F4b, not before.**
3. **How does the symbol/punctuation level interact with segments?** Punctuation
   processing is per-`Content`-segment today by assumption. Confirm that role
   and state segments must never be punctuation-processed.
4. **Does `SpeechPriority.Now` resume what it interrupted?** NVDA's does — after
   a `NOW` utterance completes, interrupted speech resumes. AURA's `Now` is
   documented as "cancel the current utterance and speak immediately", with no
   resumption. Decide deliberately; resumption is better behaviour and more work.
5. **Where does the language-run splitting actually happen** — in the renderer
   from `TextAttributes.Language` on segment attributes, or earlier in the rule
   engine? The renderer is the current assumption and is probably right, but the
   attribute has to survive that far, which it does not today.
