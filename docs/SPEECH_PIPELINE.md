# Speech Pipeline

Customizing how things sound is a core product feature, not a setting buried
three menus deep. This document defines the data model and execution order
that makes that possible.

## Goals

- **Composable.** A user rule, an app shim rule, and a script rule should
  combine predictably without one silently winning.
- **Inspectable.** A user can ask "why did it say that?" and see the rule
  chain that produced the utterance.
- **Fast.** Rule evaluation budget per event: < 5ms p99.
- **Persistable.** Rules round-trip to disk in a human-editable format.
- **Versionable.** Rule format has a schema version; old configs migrate
  forward.

## Data model

### `SpeechRequest`
Input to the pipeline.

```csharp
public sealed record SpeechRequest(
    SpeechReason Reason,            // FocusChanged, ValueChanged, ReadCharacter, ReadAll, ScriptInitiated, ...
    AccessibleNode? Node,
    string? RawText,                // for character/word/line reads
    AppContext? App,
    IReadOnlyDictionary<string, object?> Extras);
```

### `SpeechRule`
The unit of customization.

```csharp
public sealed record SpeechRule(
    string Id,                      // stable, e.g. "core.role.button"
    int Priority,                   // higher wins within a layer
    SpeechRuleScope Scope,          // role filter, state filter, app filter, regex
    SpeechRuleAction Action);       // emit, suppress, rewrite, modulate
```

### `SpeechUtterance`
Output of the pipeline, input to the engine.

```csharp
public sealed record SpeechUtterance(
    string Text,
    ProsodyHint Prosody,            // pitch, rate, volume deltas
    string? VoiceHint,              // e.g. "en-US-Neural", null = default
    SpeechPriority Priority,        // Now, Next, Background
    string? CancelGroup,            // utterances in same group cancel each other
    IReadOnlyList<string> RuleTrace);// for "why did it say that?"
```

## Layered configuration

Rules are loaded from layers and merged. Layers, evaluated in order:

1. **Built-in defaults** — ship with the binary. Cover all standard roles.
2. **User global** — `%AppData%\Aura\rules\user.yaml`.
3. **Profile** — `%AppData%\Aura\profiles\<name>\rules.yaml`.
4. **App-specific** — `%AppData%\Aura\apps\<exe>\rules.yaml`.
5. **Script-contributed** — registered at runtime by plugins.

Within a layer, higher `Priority` wins. Across layers, later layers override
earlier ones (an app rule beats a user rule beats a default). Suppression
short-circuits: a `Suppress` action ends evaluation for that scope.

## Execution

```
SpeechRequest
   │
   ▼
[ Scope filter ]              ← discard rules that don't match
   │
   ▼
[ Order by (layer, priority) ]
   │
   ▼
[ Apply actions in order ]    ← emit, rewrite, modulate, suppress
   │
   ▼
[ Compose final SpeechUtterance ]  (text, prosody, voice)
   │
   ▼
[ SpeechQueue ]
   │
   ▼
ISpeechEngine.SpeakAsync
```

Actions:

- **`Emit(template)`** — produce text. Templates support node placeholders:
  `"{role} {name}, {value}"`.
- **`Rewrite(pattern, replacement)`** — regex substitution on accumulated text.
- **`Modulate(prosody)`** — adjust pitch/rate/volume for this utterance.
- **`SetVoice(name)`** — switch voice (e.g., language-detected segments).
- **`Suppress()`** — drop the utterance entirely.

## Speech queue

The queue is not just FIFO. It supports:

- **Priority levels:** `Now` (interrupt current), `Next` (queue), `Background`
  (only speak when idle).
- **Cancel groups:** an utterance with `CancelGroup = "focus"` cancels any
  pending utterance in the same group. Prevents stale focus announcements
  when focus moves quickly.
- **Coalescing:** consecutive utterances with the same group and identical
  text within a window are deduplicated.

This is the part NVDA gets right and we should not regress: rapid focus
changes feel responsive because stale speech gets cut.

## Voice selection

Order of resolution:

1. Rule-set `VoiceHint`.
2. Detected language of the text (if multi-voice config enables this).
3. Profile default voice.
4. Engine default voice.

Language detection is opt-in (it has cost). Default off.

## Prosody and earcons

Prosody is **deltas**, not absolutes: `pitch: +10%`. Engine clamps to its
supported range. This makes rules portable across voices.

Earcons (short audio cues — "entered list", "checkbox checked") are produced
by separate `EarconRule`s evaluated in parallel with speech rules. Same
scope/priority model. Earcons mix into the same arbitration queue but on
their own audio channel so they can play simultaneously with speech.

## Persistence format

YAML for human editing. Example:

```yaml
version: 1
rules:
  - id: user.suppress.tooltips
    priority: 100
    scope:
      role: ToolTip
    action:
      type: suppress

  - id: user.rewrite.honorifics
    priority: 50
    scope:
      reason: ReadAll
    action:
      type: rewrite
      pattern: '\bMr\.\s'
      replacement: 'Mister '

  - id: user.code.lower-pitch
    priority: 80
    scope:
      role: Edit
      app: code.exe
    action:
      type: modulate
      prosody:
        pitch: -15
```

Same file format ships defaults, user rules, and app rules — only the source
differs.

## Inspection ("why did it say that?")

Every `SpeechUtterance` carries `RuleTrace` — the ordered list of rule IDs
that contributed. A debug command (`Insert+Shift+/` or similar) dumps the
trace for the last utterance to the log and optionally announces a summary.
This is how users (and we) debug rule interactions.
