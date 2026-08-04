# The foundation specs

One document per foundation named in [`../FOUNDATION.md`](../FOUNDATION.md).
Each is written so a session that has read nothing else can pick it up and
build it.

| Spec | What it is | Status | Blocks |
|---|---|---|---|
| [F1 — Output model](F1-OUTPUT-MODEL.md) | Announcements become a `Presentation`, rendered to speech, braille and a test transcript | specified | F5a, braille, audio themes, say-all resume |
| [F2 — Structured text](F2-STRUCTURED-TEXT.md) | Text carries the structure it sits inside | specified | Read mode, table navigation |
| [F3 — Navigation](F3-NAVIGATION.md) | Object navigation, and Read mode generalised to tree interceptors | specified | Read mode, terminals, documents |
| [F4 — Liveness](F4-LIVENESS.md) | Threads, COM lifetime, timeouts, backpressure | specified | everything, quietly |
| [F5 — Evidence](F5-EVIDENCE.md) | Golden transcripts, conformance suites, measured budgets, the scoreboard | specified | safe change to anything |
| [F6 — Extension](F6-EXTENSION.md) | Extension points and a deprecation policy | specified | plugin ecosystem |
| [F7 — Locale](F7-LOCALE.md) | Symbol dictionaries, character descriptions, speech dictionaries | specified | non-English use, spelling |

## How to use these

**Each spec is a contract with future sessions, not with reality.** The
contracts and invariants are meant to be held to. The implementation plans are
a considered starting point and the first real build is expected to correct
them — when it does, edit the spec in the same commit, so the next session
inherits the correction rather than the guess.

Every spec ends with **open questions the implementing session must close**.
Those are not rhetorical. They are the places where writing the spec ran out of
evidence, and answering them is part of the work.

## Build order, and why

```
F4c ─┐  (2 lines + a spike — do these on day one, in parallel; they are gates)
R2  ─┘
       F1 ──▶ F5a ──▶ F4b ──▶ F2 ──▶ F3 ──▶ 4c Read mode
                              F6 ──▶ (any time after F1)
                              F7 ──▶ (any time; long tail)
```

- **F4c and the R2 spike go first** because they are not work, they are
  *answers*. The UIA timeouts are two lines. The subtree-cache measurement can
  invalidate the whole Read-mode design, and finding that out in week six of 4c
  is the expensive way to learn it.
- **F1 before F5a** because the golden-transcript harness asserts on a
  `Presentation` rendered to text. Building the harness first means building it
  against `SpeechUtterance.Text` and then rewriting it.
- **F5a before everything else** because from that point on, every bug becomes a
  permanent test instead of a thing that might come back. This is the step that
  changes the project's failure rate.
- **F4b before F2/F3** because those two multiply the number of live COM objects
  by a large factor, and ownership is cheaper to establish over ten objects than
  ten thousand.
- **F2 and F3 before 4c** because Read mode consumes both.

## The one rule that spans all of them

Every spec adds a seam. A seam is only real if there is exactly one path
through it. Where a spec says *invariant*, it means: there must be no second
way to do this, and if a later change needs one, the seam was wrong and should
be changed rather than bypassed.

The counter-example the project already has is `CaretLineTracker` — 671 lines
and four hand-tuned timers, grown in two months, entirely because the text
model it needed did not exist and working around it was possible.
