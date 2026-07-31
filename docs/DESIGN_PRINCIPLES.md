# Design Principles

These are non-negotiable. When in doubt, read this file before deciding.

## What we will do

### Latency before features
A screen reader that responds in 200ms feels broken. The user moves focus, the
reader speaks, and the user has already moved on. We target **<50ms** from
focus event to speech start on the primary path. Every PR that touches the hot
path gets benchmarked.

Practical consequences:
- **Workstation concurrent GC**, pooled buffers on the speech and event paths.
  Not Server GC: it optimises throughput with per-core heaps and pays for it in
  pause *tails* and memory. This process is low-allocation, latency-sensitive
  and single-user, running beside the apps it reads — a pause tail is a stutter
  the user hears mid-word.
- No LINQ in tight loops. No `string` concatenation in hot paths.
- Async/await is mandatory for I/O, banned for inner-loop work.
- `PublishReadyToRun` + `TieredPGO` for startup and jitter. **NativeAOT is not
  a goal** — it is incompatible with runtime plugin loading, so it was never
  actually available. See `ASSESSMENT.md` S8.
- **Every cross-process call is timeout-bounded.** No bare `SendMessage`, no
  unbounded UIA read on the only dispatch loop. A hung application must never
  be able to silence the reader — going quiet with no explanation is the worst
  failure this program has.

### Measure, don't assert
A latency principle that is never measured is a wish. `Reader.Diagnostics.PerfTimer`
exists; the hot paths (UIA node mapping, rule evaluation, event dispatch) should
be instrumented and the numbers should settle arguments that intuition can't.

### Customization is a first-class data model
Every spoken phrase passes through a transformable pipeline. Users (and
scripts) can:
- Suppress speech for specific roles or states.
- Rewrite text via regex or scripted rules ("Mr." → "Mister").
- Adjust prosody (pitch, rate, volume) per role, per app, per context.
- Swap voices per language detected in text.
- Define entirely new gestures and bind them to commands.

This is not a "settings dialog with 200 checkboxes." It's a layered
configuration system where defaults, user prefs, app overrides, and script
overrides compose predictably.

### Scripting from day one
A scripting host exists in v0.1 even if no scripts ship with it. The contract
between core and scripts is **versioned** so a script written for v0.1 keeps
working in v2.0 or fails loudly. C# plugins first; consider Lua or JS later
for non-developer authors.

### Test against synthetic trees
NVDA tests mostly run against live applications. That's why subtle regressions
ship. We build a synthetic accessibility tree so unit tests can simulate
"focus moved from button A to combobox B in app C" without launching anything.

### Telemetry is opt-in and local-first
Structured logs (Serilog) at every layer boundary, written to a rotating local
file. No network telemetry. Users can attach the log to a bug report; we
don't see anything they don't choose to send.

That last promise is only true if spoken text never reaches disk. **Content
redaction is on by default** (`Diagnostics.RedactContent`, `Reader.Diagnostics.Redaction`)
and logs a length rather than the text. This program reads its user's banking
pages, medical records and password manager aloud; a log file that survives
reboots and gets attached to bug reports must not contain any of it. Turning
redaction off is a deliberate act for someone debugging their own machine.

### Be honest about the plugin trust model
Plugins are **trusted code running at full trust inside the host process**.
`PluginLoadContext` gives *type-identity isolation* — so a plugin's
`IAppModule` is the same type the host references, and so a plugin can be
unloaded — but it is **not a security boundary**. A loaded plugin can read the
filesystem, open sockets, P/Invoke, and reflect into host internals. The
manifest `capabilities` field is advisory and documents intent; it does not
constrain anything.

Enforcing capabilities would require a process boundary per plugin, with real
latency and complexity cost on the hot path. That may be worth doing one day.
Until it is done, the honest statement is NVDA's: *install only add-ons you
trust*. Claiming more than that in a README is worse than claiming nothing,
because users make installation decisions based on it.

## What we won't do

### We won't reinvent UIA
Modern Windows apps expose themselves through UI Automation. We use it.
Hand-rolling MSAA bindings or building parallel object models is what NVDA
inherited from a pre-UIA world. Where UIA is insufficient, we add a layer —
we don't replace.

### We won't ship features without owners
NVDA carries support for hardware that no one on the team can test. We ship
braille displays we can verify, synths we can verify, apps we can verify.
Community contributions for the rest, with a clear "best-effort" label.

### We won't allow hidden global state
No module-level mutable singletons. Every component receives its dependencies.
This is what makes the synthetic test harness possible and what keeps the
add-on contract stable.

### We won't let app shims monkey-patch core
Shims observe and override through a defined contract. They cannot reach into
core internals. This is the lesson from NVDA's appModules: powerful, but every
core change risks breaking dozens of shims invisibly.

### We won't promise cross-platform on day one
The architecture supports a Linux platform layer. We ship Windows first, prove
the abstractions, then port. Any Linux work before Windows is solid is a
distraction.

### We won't write multi-paragraph comments
Code is read more than written. Comments explain *why*, never *what*. If a
function needs a paragraph to explain it, the function is wrong.

## When this document is wrong

It will be. When you find a principle that is blocking real progress, open a
discussion before working around it. Principles can change; silent erosion is
how projects rot.
