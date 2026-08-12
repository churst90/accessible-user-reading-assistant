# AURA

**A**ccessible **U**ser **R**eading **A**ssistant — a modern, fast, scriptable
Windows screen reader written in C#.

Inspired by NVDA. Not a port. Not a fork. A clean rewrite that takes 20 years
of accessibility lessons and builds them into a foundation that won't be
fighting itself in 2030.

## Status

**Pre-alpha. It has never been used for real work by anyone.**

It builds clean, 420 tests pass, and it speaks — but it is being developed
against real hardware only in bursts, and each listening session still finds
bugs that no test caught. If you are trying it, read
[`docs/TESTING.md`](docs/TESTING.md) first, and **keep another screen reader
running**. If AURA stops speaking you need a way back that does not depend on
AURA.

Known gaps that will affect you immediately: the Windows console and PowerShell
are not readable yet, there is no browse mode for web content, and elevated
windows swallow the keyboard hook because the uiAccess certificate has not been
obtained. See [`docs/CAPABILITIES.md`](docs/CAPABILITIES.md) for the honest
scoreboard, including what will deliberately never be built.

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

Where AURA is already ahead of NVDA: data-driven speech rules layered by
user/profile/app, a versioned and isolated plugin contract with an SDK, a real
text model, a synthetic accessibility tree that makes core logic testable with
no Windows and no applications, and golden-transcript regression tests — which,
as far as we know, no other screen reader has.

## Building and running

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/churst90/accessible-user-reading-assistant.git
cd accessible-user-reading-assistant

dotnet build AURA.slnx
dotnet test  AURA.slnx --no-build
dotnet run --project src/ReaderHost.Windows
```

Press **Reader+Q** (`Insert+Q` or `CapsLock+Q`) to quit. Bindings are in
[`docs/KEYMAP.md`](docs/KEYMAP.md); **Reader+A** opens the Aura menu.

Optionally install [eSpeak NG](https://github.com/espeak-ng/espeak-ng/releases)
for a second synthesiser. Without it you get SAPI 5 only, silently.

### Developing from Linux

The Windows projects compile on Linux — pass `EnableWindowsTargeting` and the
SDK pulls the Windows reference packs:

```bash
dotnet build AURA.slnx -p:EnableWindowsTargeting=true
dotnet test  AURA.slnx --no-build     # 420 passed, 1 skipped
```

They cannot *run* there, and `ReaderPlatform.Windows.Tests` targets
`net10.0-windows` so it does not execute either. But it turns "I changed a
Windows file and hope it compiles" into a checked fact, which is worth a great
deal when most of the work is on Windows-only code.

## Repository layout

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full breakdown.

```
docs/        Design documents (read these first)
src/         C# projects — abstractions, core, platform, host, UI, plugins
tests/       Unit + golden-transcript + synthetic-tree tests
native/      Unavoidable C++ shims (DLL injection target)
assets/      Icons, sounds, default speech rules
templates/   `dotnet new` template for plugin authors
samples/     Example plugins
installer/   WiX 4 MSI
scripts/     Build/dev tooling
```

## What to read in order

1. [`docs/SESSION_HANDOFF.md`](docs/SESSION_HANDOFF.md) — **read first** — current state, what was heard on hardware, next concrete move
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
13. [`docs/KEYMAP.md`](docs/KEYMAP.md) — default key bindings, checked against the code by a test
14. [`docs/TESTING.md`](docs/TESTING.md) — for the first people trying it
15. [`docs/ROADMAP.md`](docs/ROADMAP.md) — phased milestones

## Reporting a problem

Press **Ctrl+Reader+D**. That copies a diagnostic snapshot to the clipboard —
version, synthesiser, voice, rate, keyboard layout, focused application, log
location — and paste it into the issue.

It deliberately contains **no spoken text and no control content**. A screen
reader reads banking pages and password managers aloud; none of that belongs in
a bug report. Describe what was announced in your own words.

The most useful report is what you *heard*: "arrowing the desktop said the
tooltip instead of the icon name" has fixed more bugs here than any stack trace.

## License

[GPL-3.0](LICENSE). AURA is an independent implementation and shares no code
with NVDA, but it is built for the same users and released under a copyleft
licence for the same reason: a screen reader that people depend on should not
be something anyone can take away from them.
