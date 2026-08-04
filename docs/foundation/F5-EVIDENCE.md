# F5 — Evidence

**Status:** F5a **built** 2026-08-03 — `tests/Reader.Transcripts.Tests`, eight
scenarios, and it caught a live bug on its first run (see below). F5b, F5c and
F5d still to do; F5c can start immediately on the VM.
**Depends on:** F1 for F5a (the transcript renders a `Presentation`).
**Blocks:** safe change to anything.

---

## Why

Every hardware test this project has run has found real bugs in code that had
passing tests and a clean build. Four rounds of it, with a consistent shape: a
locally reasonable fix caused the next bug. Excluding arrows from speech-cancel
made speech lag an item behind; cancelling on every key then raced the
announcement and produced silence; a dedup rule added to quiet list boundaries
silenced consecutive blank lines.

None of those were caught by 338 passing tests, because none of those tests ask
the question that matters:

> **Given this tree and these keystrokes, what did the reader say, in what
> order?**

That is answerable, deterministically, with no Windows and no audio, the moment
`Presentation` and `TranscriptRenderer` exist. It is the single highest-leverage
thing available to this project and no competitor has it — NVDA's own test suite
does not assert announcement behaviour broadly, which is why NVDA ships
announcement regressions every release.

---

## F5a — Golden transcripts

### The shape

A scenario is a synthetic tree, an input script, and the expected output.

`tests/Transcripts/blank-lines-each-say-blank.transcript`

```
# Arrowing up through consecutive blank lines says "blank" every time.
# Regression: commit c8a54e1 — the arbiter's duplicate-text rule swallowed
# every repeat, so only the first blank line was announced.

surface: string
  content: |
    first line
    
    
    last line
  caret: end

input:
  Up
  Up
  Up

expect:
  CaretMoved   | last line
  CaretMoved   | blank
  CaretMoved   | blank
  CaretMoved   | first line
```

The format is deliberately plain text, not a serialised object graph, because
**the artifact that gets reviewed is the diff**. A reviewer looking at

```
-  CaretMoved   | blank
-  CaretMoved   | blank
+  CaretMoved   | blank
```

sees the bug immediately. A JSON diff does not read that way.

### The runner

`tests/Reader.Transcripts/TranscriptRunner.cs`

```csharp
[Theory]
[TranscriptData("Transcripts")]      // one case per .transcript file
public void Transcript(TranscriptCase c)
{
    var host = new HeadlessReader(c.BuildProvider(), c.BuildSurfaces());
    foreach (var gesture in c.Input) host.Send(gesture);
    host.Drain();
    host.Transcript.Should().BeEquivalentTo(c.Expected);
}
```

`HeadlessReader` is the piece to get right: the real dispatch loop, the real
rule engine, the real `OutputArbiter`, the real `SpeechQueue` — with the engine
replaced by `TranscriptRenderer` and the clock replaced by a `TimeProvider` the
test drives. Anything stubbed out is a place a bug can hide, so stub as little
as possible. The arbiter's coincidence window and the queue's coalescing are
*exactly* where the bugs have been, so they must be real and the clock must be
fake.

`AURA_UPDATE_TRANSCRIPTS=1 dotnet test` rewrites the expectations. That is the
authoring workflow and the review workflow both.

### The first ten scenarios

Not arbitrary. These are the bugs that have already happened, plus the
behaviours most likely to break next.

| Scenario | Guards |
|---|---|
| `blank-lines-each-say-blank` | commit c8a54e1 — over-suppression |
| `list-boundary-is-silence` | commit c8a54e1 — the boundary must be *silence*, not a repeat |
| `backspace-announces-deleted-char` | roadmap 3.6 #6, and commit f1b38d4's silence |
| `focus-plus-selection-is-one-announcement` | the arbiter's whole reason to exist |
| `arrow-through-icon-toolbar` | ASSESSMENT S5 — unnamed controls must not dedup away |
| `left-arrow-at-line-start-is-a-line-move` | `CaretMotionResolver`, a known-wrong case |
| `emoji-is-one-character` | surrogate pairs; already tested at the surface, not end to end |
| `stale-focus-announcement-is-dropped` | F1's validity predicate |
| `run-dialog-refocus-does-not-repeat` | the case the old dedup was built for |
| `typing-echoes-once` | key echo vs. caret follow, two producers |

Every future bug adds one file. **That is the discipline: a bug is not fixed
until it has a transcript.**

---

## F5b — Conformance suites, not per-backend tests

`TEXT_MODEL.md` describes `StringTextSurfaceTests` as "the conformance suite
every other backend should pass an equivalent of." An *equivalent* is how
backends drift. It should be the same tests:

```csharp
public abstract class TextSurfaceConformance
{
    protected abstract ITextSurface Create(string content, int caret);

    [Fact] public void Line_expansion_excludes_the_newline() { … }
    [Fact] public void A_word_is_a_run_of_non_whitespace()   { … }
    [Fact] public void An_emoji_is_one_character()           { … }
    [Fact] public void Move_reports_how_far_it_actually_got(){ … }
    // …the existing 21, unchanged in substance
}

public class StringSurfaceConformance : TextSurfaceConformance { … }       // neutral
public class Win32SurfaceConformance  : TextSurfaceConformance { … }       // Windows suite
public class UiaSurfaceConformance    : TextSurfaceConformance { … }       // Windows suite
public class ReadModeBufferConformance: TextSurfaceConformance { … }       // when 4c lands
```

The Windows instantiations need a real control, which means a small WinForms/WPF
harness app in `tests/integration/`. That has been on the roadmap since Phase 0
and has never been built; it is now on the critical path, because `UiaTextSurface`
and `Win32TextSurface` are both shipped and **neither has ever been tested
against a real control.**

Same pattern for `IAccessibilityProvider` once F3 adds the tree walk.

---

## F5c — Budgets, measured *(can start immediately)*

`<50 ms from focus event to speech start` has been the headline principle since
day one and has never been measured. `PerfTimer` exists and is unused on the hot
path. The cache-request change that was supposed to turn ~27 round trips into 1
is still recorded as "not yet measured".

Named stages, each with a budget:

| Stage | Budget | Where |
|---|---|---|
| UIA event → dispatch loop | 5 ms | `NativeUiaProvider.Queue` → dequeue |
| Node mapping (cached) | 5 ms | `NativeUiaNodeMapper.Map` |
| Rule evaluation → `Presentation` | 5 ms | `SpeechRuleEngine` |
| Render → `Utterance` | 2 ms | `SpeechRenderer` |
| Queue → engine `SpeakAsync` | 5 ms | `SpeechQueue` drain |
| **Total, focus to first audio** | **50 ms** | end to end |

Always-on in a debug build, sampled in release, and written to the log at a
level below the redaction threshold. A `Reader+F12`-style "report last latency"
command makes it checkable by ear during a VM session, which is worth more than
a CI number nobody looks at.

### The decision gate, which is not a budget

**R2: the cost of building a virtual buffer cross-process.**

NVDA injects a DLL into every process specifically because building its buffer
across the process boundary was too slow. AURA has deferred injection
"indefinitely" on *coverage* grounds — a different question from *cost*.

The spike, in full:

1. Open a large real page (an MDN reference, a long Wikipedia article) in Edge.
2. Build a `CacheRequest` with `TreeScope_Subtree`, naming the properties a
   buffer needs — control type, name, value, `IsOffscreen`, bounding rect,
   `PositionInSet`, `SizeOfSet`, `Level`, plus the text attributes.
3. `BuildUpdatedCache` on the document element. Time it.
4. Walk the cached subtree, flattening to text. Time it separately.
5. Repeat after a DOM mutation to get the *re-render* cost, which is what
   actually matters — pages mutate constantly and a rebuild happens per mutation.

**Numbers that decide things:** under ~200 ms for a full build and under ~50 ms
for a subtree re-render means the UIA-only plan holds. Over a second means Read
mode needs either incremental rebuilds far more sophisticated than planned, or
the injection path AURA has said it will not build. **Find this out before
designing 4c, not during it.**

---

## F5d — The scoreboard

`docs/CAPABILITIES.md` — every capability NVDA or JAWS has, one row each, with a
status of `shipped` / `planned` / `deliberately never`.

The `deliberately never` column is the one that does work. AURA implements
roughly 8 of NVDA's ~45 subsystems; without an explicit never-list, the other 37
are all implicit promises, and the project spends its life feeling behind
instead of choosing. Written down, most of them turn out to be things that
*should* never be built — which converts a deficit into a design.

Reviewed at the end of each phase. A row that moves from `never` to `planned` is
a decision that should be visible.

---

## Migration

1. **F5c PerfTimer instrumentation** and the R2 spike. Immediately, on the VM;
   independent of everything else.
2. **`TranscriptRenderer`** (part of F1).
3. **`HeadlessReader` + runner + the first two transcripts** — the two bugs from
   the last two commits. Proves the harness catches something that actually
   escaped.
4. **Backfill the other eight.**
5. **`TextSurfaceConformance`** refactor; the neutral instantiation first.
6. **`tests/integration/` harness app** and the two Windows instantiations.
7. **`CAPABILITIES.md`**, and review it at each phase end.

---

## Proof it landed

- Reverting commit c8a54e1's arbiter change makes
  `blank-lines-each-say-blank.transcript` fail. *(If it does not, the harness is
  not testing what it claims to.)*
- `Win32SurfaceConformance` and `UiaSurfaceConformance` run in CI on a Windows
  runner and pass the same 21 assertions as the string backend.
- The focus-to-speech number is in the log, and it is a number, not a principle.
- `CAPABILITIES.md` has a non-empty `deliberately never` column.

---

## Open questions the implementing session must close

1. **How much of the host can `HeadlessReader` reuse?** `Program.cs` is 785
   lines of wiring and none of it is currently reachable without a Windows
   message loop. Extracting a testable core may be a prerequisite, and if so it
   is real work that this spec is currently hiding.
2. **How is timing represented in a transcript?** The arbiter has a 120 ms
   coincidence window and the queue coalesces. The input script needs to be able
   to say "these two events arrived 5 ms apart" and "the user waited a second".
   A fake `TimeProvider` plus explicit `wait: 200ms` lines is the assumption.
3. **Do transcripts assert on order only, or on grouping into utterances?**
   Grouping is where interruption bugs live, so probably both — but that makes
   the format less readable and the trade should be made deliberately.
4. **Does the integration harness app need to be signed/manifested** to be read
   by a UIA client running in the same session? Probably not, but a UAC-elevated
   test runner would fail confusingly.
