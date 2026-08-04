# Aura

A modern, fast, scriptable Windows screen reader written in C#.

Inspired by NVDA. Not a port. Not a fork. A clean rewrite that takes 20 years
of accessibility lessons and builds them into a foundation that won't be
fighting itself in 2030.

## Status

Pre-alpha. It builds and its unit tests pass, but it has not been used for real
work by anyone. If you are trying it, read
[`docs/TESTING.md`](docs/TESTING.md) first — and keep another screen reader
running.

## Design pillars

1. **Latency is the product.** Every hot path is measured. GC pauses are budgeted.
2. **Customization is structural, not bolted on.** Speech rules, key maps, and
   scripts are first-class from v0.1 — not retrofitted at v3.
3. **The accessibility tree is abstracted.** Core code never sees a UIA type.
   That is what makes Linux possible later, and what makes the test harness
   possible *now*.
4. **App shims are plugins, not patches.** Versioned contract, isolated load,
   hot-reloadable in dev.
5. **No legacy.** If a feature has no concrete user need today, it does not
   exist. Add when needed; design for it from the start.

## Repository layout

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full breakdown.

```
docs/        Design documents (read these first)
src/         C# projects
native/      Unavoidable C++ shims (DLL injection target)
tests/       Unit + integration + synthetic-tree tests
assets/      Icons, sounds, default config
scripts/     Build/dev tooling
```

## What to read in order

1. [`docs/SESSION_HANDOFF.md`](docs/SESSION_HANDOFF.md) — **read first** — current state, next concrete move
2. [`docs/NVDA_ANALYSIS.md`](docs/NVDA_ANALYSIS.md) — what NVDA is made of, what to take, what to avoid, and how far the gap actually is
3. [`docs/CAPABILITIES.md`](docs/CAPABILITIES.md) — the scoreboard, including what we will deliberately never build
4. [`docs/FOUNDATION.md`](docs/FOUNDATION.md) — **the plan** — the seams and harnesses to build before more features
5. [`docs/ASSESSMENT.md`](docs/ASSESSMENT.md) — outside architectural review, ranked by consequence
6. [`docs/DESIGN_PRINCIPLES.md`](docs/DESIGN_PRINCIPLES.md) — what we will and won't do
7. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the layering and key abstractions
8. [`docs/TEXT_MODEL.md`](docs/TEXT_MODEL.md) — the text range contract and its migration path
9. [`docs/OUTPUT_PIPELINE.md`](docs/OUTPUT_PIPELINE.md) — the single path every spoken word travels
10. [`docs/READ_WRITE_MODES.md`](docs/READ_WRITE_MODES.md) — Read/Write mode framework (Phase 4c)
11. [`docs/EXTENSIONS.md`](docs/EXTENSIONS.md) — plugin model, maintainability rules, NVDA Remote interop
12. [`docs/SPEECH_PIPELINE.md`](docs/SPEECH_PIPELINE.md) — customization model
13. [`docs/ROADMAP.md`](docs/ROADMAP.md) — phased milestones
14. [`docs/FIRST_STEPS.md`](docs/FIRST_STEPS.md) — Phase 0 checklist (already done)
