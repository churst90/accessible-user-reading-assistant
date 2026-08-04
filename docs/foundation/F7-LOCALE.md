# F7 — Locale and pronunciation data

**Status:** specified, not built. Format decisions are urgent; content is a long
tail.
**Depends on:** F1 (symbol processing happens in `SpeechRenderer`).
**Blocks:** any non-English use, spelling, and reading anything with punctuation
in it — which is all code, all URLs, and most of the web.

---

## Why

AURA has a **better rewriting engine than NVDA** — YAML rules, layered by
user/profile/app, with a rule trace — running on **no locale data at all**.
`PunctuationFilter` exists; symbol dictionaries, character descriptions and user
speech dictionaries do not.

The engine is the hard part and it is built. The data is the long part and it
has not started. It is miserable to retrofit because rules, tests and — after
F5a — transcripts all reference it: change the symbol level model later and
every transcript that contains punctuation changes with it.

So the urgent part of this spec is **the format**, not the content. Ship `en`,
freeze the shape, then let translators fill in the rest.

Four distinct things, routinely conflated:

| Thing | Question it answers | Example |
|---|---|---|
| **Symbol dictionary** | How is this character pronounced, and at what verbosity? | `#` → "number", level *most* |
| **Character description** | How is this character disambiguated when spelling? | `b` → "bravo" |
| **Speech dictionary** | What user-defined substitutions apply to text? | "NVDA" → "en vee dee ay" |
| **Symbol level** | How much punctuation is spoken at all? | none / some / most / all / character |

---

## The contract

### Symbol level

```csharp
public enum SymbolLevel
{
    None = 0,       // no punctuation spoken
    Some = 100,     // only what changes meaning: % $ etc.
    Most = 200,     // most punctuation
    All = 300,      // everything
    Character = 400,// everything, including what is normally silent — spelling mode
}
```

Numeric gaps are NVDA's design and worth keeping: a symbol entry declares the
*minimum* level at which it is spoken, so a locale can introduce an intermediate
tier without renumbering.

### Symbol dictionary format

`assets/locale/<bcp47>/symbols.yaml`

```yaml
# level: the minimum SymbolLevel at which this is spoken
# preserve: whether the original character is also sent to the synth —
#   "never", "always", or "afterSpeaking" (for characters that affect prosody)
symbols:
  - char: "."
    speak: "dot"
    level: some
    preserve: afterSpeaking   # the period still ends the sentence
  - char: ","
    speak: "comma"
    level: most
    preserve: afterSpeaking
  - char: "#"
    speak: "number"
    level: most
  - char: " "
    speak: ""
    level: character          # non-breaking space: silent, but not nothing
  - pattern: "\\b(\\d+)/(\\d+)\\b"
    speak: "$1 out of $2"
    level: some
```

`preserve: afterSpeaking` is the subtle one and the reason not to invent a
format from scratch: replacing `.` with the word "dot" and dropping the
character removes the sentence break, and the synth then runs sentences
together. NVDA learned this and encoded it; copy the model, not the file.

### Character descriptions

`assets/locale/<bcp47>/characters.yaml`

```yaml
characters:
  a: ["alpha"]
  b: ["bravo"]
  "0": ["zero"]
  # CJK entries carry several, spoken in order, because one is not enough
  "中": ["中国的中", "中心的中"]
```

Used when spelling with descriptions (a "spell with phonetics" command, and
automatically on a second press of "read current character" — the double-tap
convention every screen reader shares).

### User speech dictionaries

Three tiers, resolved in order, all the same format:

```
%AppData%\Aura\dictionaries\default.yaml     always active
%AppData%\Aura\dictionaries\voice-<id>.yaml  active for one voice
                              (temporary)     session only, from the dialog
```

```yaml
entries:
  - match: "NVDA"
    replace: "en vee dee ay"
    type: word          # exact | word | regex
    caseSensitive: true
```

These are *user* data and must never be shipped-over on update — a user's
pronunciation fixes are hard-won and losing them is a real harm.

### Resolution order

Last wins, mirroring the existing config layering so there is one mental model:

```
built-in locale  →  built-in locale variant  →  user locale override
                 →  voice dictionary  →  temporary dictionary
```

### Where language comes from

Three sources, in priority order:

1. `TextAttributes.Language` on the run — the document's own declaration.
2. The app module's declared language.
3. The UI language.

This is why F1's `LanguagePart` and F2's per-run attributes matter here: without
them, a mixed-language line cannot be pronounced correctly no matter how good
the dictionaries are.

---

## How it will be implemented

`Reader.Speech/Locale/`

| File | Contents |
|---|---|
| `SymbolDictionary.cs` | Load, merge layers, compile to a single lookup |
| `SymbolProcessor.cs` | Apply to a `Content` segment at a given level; the hot path |
| `CharacterDescriptions.cs` | Load and look up |
| `SpeechDictionary.cs` | The three user tiers |
| `LocaleData.cs` | Resolution and caching, keyed by BCP-47 with fallback (`en-GB` → `en`) |

`SymbolProcessor` is on the hot path for every announcement containing text. It
must compile to a single pass — a character lookup table for single-character
entries and one combined regex for pattern entries, built once per locale and
cached. `DESIGN_PRINCIPLES.md` bans LINQ and string concatenation in hot loops;
this is where that bites hardest.

**Only `SegmentKind.Content` is symbol-processed.** Role, state, position and
structure segments are the reader's own words; running them through a symbol
dictionary would turn "3 of 10" into something else in a locale where it should
not.

### Content

Ship `en` complete. Ship nothing else. A half-translated symbol dictionary is
worse than none, because the user cannot tell which half.

Translation infrastructure — where the files live, how a translator submits, how
completeness is measured — is its own piece of work and belongs after there is a
user asking in a language AURA does not support.

---

## Migration

1. **Define the formats and load them** — no consumer yet.
2. **Write `en` symbols and character descriptions.** Steal the *structure* of
   NVDA's `locale/en/symbols.dic` and `characterDescriptions.dic`; write the
   content fresh (NVDA is GPL and AURA is a clean-room rewrite).
3. **Replace `PunctuationFilter` with `SymbolProcessor`** in `SpeechRenderer`.
   Behaviour changes here — verify by ear on the VM, and it will need transcripts.
4. **Character descriptions** + the double-tap "describe character" command.
5. **User speech dictionaries** + a settings panel.
6. **Per-language voice selection**, using F1's `LanguagePart` and the
   `EngineRouter` that already exists.

---

## Proof it landed

- At level `some`, `Hello, world.` speaks as "Hello world" with a sentence break
  still audible; at `all`, "Hello comma world dot".
- A non-breaking space is silent and does not become "blank".
- Reading a character twice quickly says "b" then "bravo".
- A user dictionary entry survives an update.
- `SymbolProcessor` over a 200-character line stays inside its slice of the
  render budget. *(Measured — this is the one piece of F7 with a latency risk.)*
- Role segments are unaffected by symbol level.

---

## Open questions the implementing session must close

1. **Does `preserve: afterSpeaking` work across SAPI 5 and eSpeak NG
   identically?** The whole point is retaining prosody, and the two engines
   handle a trailing period differently. May need per-engine handling, which
   would be unfortunate and should be discovered early.
2. **Is symbol processing per-segment or per-utterance?** Per-segment is assumed
   above and is cleaner; but a symbol at a segment boundary (a comma the rule
   engine inserted between name and role) then gets processed as content.
3. **How does symbol level interact with Read mode?** Reading a web page at
   level `most` is unbearable; NVDA's answer is a per-context default. Probably a
   config layer keyed on interceptor, which the layering already supports.
4. **Should character descriptions be locale data or voice data?** For CJK they
   are locale; for phonetic alphabets some users want their own. Probably locale
   with a user override tier, matching the dictionary model.
5. **What is the fallback when a BCP-47 tag has no data at all?** Silent
   fallback to `en` mispronounces; refusing to speak is worse. Decide.
