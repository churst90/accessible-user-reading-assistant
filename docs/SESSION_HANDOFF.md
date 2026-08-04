# Session Handoff

Last updated: 2026-08-03 (F1 + F5a landed)

---

## Round 2 — the four things heard on hardware, and what changed

First listening session on F1 found four. All were real; three were in the caret
path, which none of the F1 work had touched.

| Heard | Cause | Fix |
|---|---|---|
| Blank lines don't read at all | A provider expanding an empty line returns the line terminator. `"\r\n"` is not `IsNullOrEmpty`, so the reader spoke two characters that make no sound | `ToRequest` uses `Blank.Is`, and trims the terminator off a line |
| Wrapping to the previous line reads the whole line | The unit was inferred from ground covered, so Left crossing a newline reported a line move | The keystroke supplies the granularity; positions still say what happened |
| End of line said nothing useful | — | Says "line feed" |
| Desktop icon name replaced by its tooltip, intermittently | `ToolTipOpened` was mapped onto `AlertRaised`; alerts are `Now`, and `Now` cuts off what is playing | Tooltips are their own event kind and reason, at `Background`. They wait |

**The model correction worth keeping.** `TEXT_MODEL.md` said the keystroke
carries no meaning and the position tells you everything. That is half right and
the missing half was load-bearing: **positions say what happened, the keystroke
says what granularity was asked for.** Left is a character move even when it
wraps, and answering a one-character request with a paragraph is what Cody
heard. A caller with no key behind it — a mouse click, a find result — still
falls back to inference, which is the best answer available when nobody asked.

Also fixed unheard: notification text now goes on the node's `Name`. Every rule
that announces a non-focus event reads `{name}`, so text carried only in
`CaretLine` was structurally unreachable and toasts said nothing.

### What to check this round

1. **Notepad, arrow up and down through blank lines** — "blank" each time.
2. **Left-arrow at the start of a line** — one character or "line feed", never
   the whole line above.
3. **Press End** — "line feed".
4. **Arrow across the desktop** — the icon name, then its tooltip after,
   never instead.
5. **Everything in the round-1 table below** still holds.

Two things I still cannot check from Linux: whether real toasts now speak, and
whether fast typing with character echo on backs up (echo has no cancel group,
so a burst queues; it behaved this way before F1 too).

---

## Round 1 — what to listen for

**F1 changed how interruption works, everywhere.** Nothing else in this round is
risky; this is. Test it first and stop if it is wrong.

The old behaviour answered *"is this announcement still wanted?"* by cancelling
speech on every keypress. The new behaviour splits that into two mechanisms that
do different jobs — a keypress still cancels the **sound that is playing**, and a
**validity predicate** decides which **queued** announcements are still worth
speaking, asked at the moment each would be spoken.

| Try this | Expected | If it is wrong |
|---|---|---|
| Arrow quickly through a folder in Explorer | Only the item you land on. No backlog, no reading an item behind | The predicate is not matching — check `FocusTracker.OnFocusChanged` runs *before* `SweepInvalid` |
| Arrow up through consecutive blank lines in Notepad | "blank" **every time** | The queue's content suppression is back somehow |
| Tab across a toolbar of unnamed icon buttons | "button" every time | Same as above |
| Press Down on the last item of a list | Silence | The keypress cancel is not firing |
| Backspace over text | The deleted character | Something is cancelling the announcement the keystroke caused |
| Win+R | The dialog title, then the edit | The window announcement is being swept as stale — it should be exempt as the owning window |
| Let a toast or notification appear while focus is elsewhere | It speaks | "Never had focus" is not exempting it |
| Anything with capital letters, read by character | Higher pitch on capitals, no "cap" | Prosody spans are not reaching the engine |

**Two things I could not check from Linux and would like an ear on:**

1. **Whether a UIA notification's text arrives at all.** `core.alert.raised` and
   `core.liveregion.changed` emit `{name}`, but `NativeUiaProvider.HandleNotification`
   puts the notification's spoken text in `CaretLine`, which becomes `{text}`.
   If toasts are silent, that is why, and the fix is a rule, not code.
2. **Fast typing with character echo on.** Echo announcements have no cancel
   group, so a burst of keystrokes queues rather than superseding. It behaved
   this way before too, so it is not a regression — but it may be audible now
   that nothing else is dropping them.

---

This is the load-on-startup brief for whoever picks up next. Read it before
opening anything else.

---

## Where we are: feature work is paused; the foundation is specified

An NVDA analysis ran on 2026-08-03 and produced a plan. **Do not start feature
work without reading it.** In order:

1. [`NVDA_ANALYSIS.md`](NVDA_ANALYSIS.md) — what NVDA is, what to take, what to
   avoid, and the honest size of the gap.
2. [`CAPABILITIES.md`](CAPABILITIES.md) — the scoreboard, including the
   deliberate never-list.
3. [`FOUNDATION.md`](FOUNDATION.md) — the seven foundations and the order.
4. [`foundation/`](foundation/) — one implementation spec per foundation:
   contracts in real C#, file-by-file plan, migration, proof, and the open
   questions the implementing session has to close.

**Build and test status**

```
dotnet build AURA.slnx -p:EnableWindowsTargeting=true   →  0 Warning(s)  0 Error(s)
dotnet test  AURA.slnx --no-build                       →  338 passed, 1 skipped, 0 failed
```

### The three findings that changed the plan

**1. Two contract holes block almost everything.** `SpeechUtterance` is a flat
string — it cannot carry a language change, a prosody span, an inline earcon, a
say-all resume marker, or a validity predicate. `ITextRange.GetAttributes()` is
flat per-range — it cannot say what structure was entered and left. Those are
F1 and F2, and braille, audio themes, say-all resume, per-language voices, Read
mode and table navigation are all downstream of them.

**2. The recurring speech bugs are one bug, and it is not a timing bug.**
Cancel on keypress, exclude arrows, un-exclude arrows, make the cancel
synchronous, add a duplicate-text window, remove it because blank lines went
silent — every round was an attempt to answer *"is this announcement still
wanted?"* with timing. NVDA answers it with state: an announcement carries a
predicate that the queue evaluates at the moment it would be spoken. Stale ones
evaporate; valid ones survive; there is no race because there is no timing.
**Do not attempt a seventh tuning pass.** It needs F1.

**3. Object navigation is missing entirely.** Not a missing feature — a missing
*axis*. NVDA's navigator object walks the tree independently of focus, and it is
how a user reads a status bar, inspects a toolbar, or investigates a control
that announces nothing. This is F3, and it is among the first things a switching
NVDA user will reach for and not find.

### What to do next, in order

1. **Measure, on the VM.** `PerfTimer` on the hot path (F5c) and the R2 spike —
   the cross-process cost of a `TreeScope_Subtree` `BuildUpdatedCache` over a
   large page. R2 can invalidate the whole Read-mode design and it is cheap to
   run. Both are gates, not work; do them before building.
2. **Start the uiAccess certificate.** Weeks of lead time, nothing gates on it,
   and without it the keyboard hook does not fire in any elevated window — the
   reader looks frozen and the user cannot even stop speech.
3. **Build F1** ([`foundation/F1-OUTPUT-MODEL.md`](foundation/F1-OUTPUT-MODEL.md)).
   Everything else keys off it, including the test harness.
4. **Build F5a** — golden transcripts. From that point every bug becomes a
   permanent test. The first two files backfill the last two commits' bugs.
5. Then F4b (COM ownership) → F2 → F3 → 4c.

### Landed 2026-08-03

- **Read/Write mode rename.** `ReaderMode.Type` → `ReaderMode.Write` (`Write = 0`
  keeps the safe default), spoken string "write mode",
  `READ_TYPE_MODES.md` → `READ_WRITE_MODES.md`. Cody's call, and right: the pair
  has to read as a pair, it covers cases that are not literally typing, and
  `ReaderMode.Type` next to `System.Type` helped nobody.
- **UIA connection and transaction timeouts** (`NativeUia.TrySetTimeouts`,
  F4c). The dispatch loop could previously block forever against a hung
  provider — the one path `ASSESSMENT.md` S1's `SendMessageTimeout` fix did not
  cover. The values are a starting point and should be set from measurement.

### Known-broken, and deliberately not fixed one at a time

These are all symptoms of the missing seams above. Fix them against the model,
not individually — that is what produced the last four rounds of regressions.

- Notepad reads a whole line on left-arrow to the line above.
- Blank lines unreliable. *(F1: blankness is a property of the composed
  presentation, not of a string compared with the last string.)*
- Line endings not announced.
- PowerShell numpad review dead. *(F3: the console is the first tree
  interceptor, and it should be built before Read mode.)*
- Backspace/Delete announcement missing (roadmap 3.6 #6). *(F1: the deleted text
  is a segment captured before the keystroke.)*

---

## History: architectural pass done; Phase 3.6 mostly closed *(2026-07-30)*

An outside architectural review (`docs/ASSESSMENT.md`) ran on 2026-07-30 and
its findings were implemented in the same pass. Phase 3.6 items #1–#4 and the
`ITextAccess` refactor are closed; #5 is partly done and a new #6 was opened.

**Build and test status**

```
dotnet build AURA.slnx      →  Build succeeded.  0 Warning(s)  0 Error(s)
262 tests passing across the platform-neutral suites (was 195)
```

### The one operational thing to know first

**The Windows projects compile on Linux.** Pass
`-p:EnableWindowsTargeting=true` and the SDK pulls the Windows reference packs:

```
dotnet build AURA.slnx -p:EnableWindowsTargeting=true          # everything
dotnet build src/ReaderHost.Windows -p:EnableWindowsTargeting=true \
    -r win-x64 --self-contained false                                # publish shape
```

They cannot *run* there, and the WPF/UIA behaviour still needs a real machine.
But it turns "I changed a Windows file and hope it compiles" into a checked
fact, which is worth a great deal when most of the work is on Windows-only
code. Everything below was compile-verified this way.

### What changed

**Liveness — a hung app could permanently silence the reader.**
- Every cross-process Win32 read now goes through `Interop/Win32Text.cs`, which
  uses `SendMessageTimeout` with `SMTO_ABORTIFHUNG`. There is no bare
  `SendMessage` left in the tree. The old `EM_GETSEL` also truncated at 65,535
  characters; the pointer form fixes that.
- `ResponsivenessWatchdog` (Core, 8 tests) notices "input arrived, no speech
  followed" and beeps. Deliberately a beep and not speech — speech is what may
  be wedged.

**Latency — ~28 cross-process round trips per focus event became one.**
- `UiaCache.cs` holds a single `CacheRequest`; every event subscription is
  registered under it, so elements arrive with their whole property set.
- `UiaNodeMapper` was rewritten to read only *properties*, never pattern
  objects, and to prefer `.Cached.*`. It falls back to live reads if a provider
  ignores the cache.
- **Not yet measured.** Instrument with `PerfTimer` on real hardware and
  confirm before trusting the number.

**The text model — the architectural change.**
- `Reader.Abstractions/Text/`: `ITextRange`, `ITextSurface`,
  `ITextSurfaceProvider`, plus `StringTextSurface` as the reference
  implementation and conformance target.
- `ReaderPlatform.Windows/Text/`: `UiaTextSurface` (TextPattern),
  `Win32TextSurface` (`WM_GETTEXT` + `EM_GETSEL`, wrapping `StringTextSurface`),
  and `UiaTextSurfaceProvider`, which owns the whole fallback chain that used
  to be open-coded in three places.
- `CaretLineTracker` (671 lines) is **deleted**. `CaretTracker` +
  `CaretMotionResolver` + `CaretFollowService` replace it by comparing observed
  positions instead of classifying keystrokes. All four timing constants and
  the cross-component suppression window are gone.
- `ReviewCursor` is now an `ITextRange`, so it follows the caret for free and
  no longer reads stale text after an edit.

**Focus dedup was dropping real focus changes.** The old `(Role, Name)` sliding
750 ms window matched every unnamed control of the same role against every
other one — a toolbar of icon buttons went silent after the first. Now keyed on
role + name + automation id + bounding rectangle, with no time window.

**Events that fire on unfocused elements.** Every subscription had been scoped
to the focused element, which made alerts and toasts structurally unreachable.
`WindowOpened`, `SelectionItem.ElementSelected`, `MenuOpened` and
`ToolTipOpened` are now desktop-wide, with rules in `defaults.yaml`.

**Privacy — spoken text was being written to disk.** `Program.cs` logged
`utterance.Text` at `Warning`, above the configured minimum. `Redaction`
(default **on**, `Diagnostics.RedactContent`) replaces content with a length.
No hashing: a digest of a single announced character is trivially reversible.

**uiAccess.** `app.manifest` (dev, `uiAccess=false`) and
`app.uiaccess.manifest` (shipping, `uiAccess=true`), selected by
`-p:UiAccess=true`. The split matters — an unsigned uiAccess binary does not
launch at all, so a dev build must not request it. Signing and install
requirements are in `docs/UIACCESS.md`; **the certificate is the long pole and
nothing gates on it yet.**

**Corrected assumptions.** NativeAOT dropped (it cannot coexist with runtime
plugin loading); Server GC → workstation concurrent; the contract assembly
moved `netstandard2.1` → `net8.0` and the `IsExternalInit` polyfill deleted;
the plugin trust model stated honestly in `DESIGN_PRINCIPLES.md`.

### What to do next

1. **Run it on Windows and listen.** Everything above is compile-verified and
   unit-tested; none of it has been heard. The acceptance test is the one the
   roadmap already names — Notepad, the Run box, arrowing, dialogs.
2. **Measure the cache-request change** with `PerfTimer` before believing it.
3. **Phase 3.6 #6** — backspace/delete announcement, regressed deliberately by
   the caret rewrite. It belongs in key echo, not in position diffing.
4. **Native `IUIAutomation` COM migration.** Still the gate on browse mode:
   live regions, notifications, heading level and link attributes have no
   managed-API equivalent. `UiaTextRange.GetAttributes` documents exactly
   where this bites.

## History: Phase 2 closed, Phase 3 closed, pre-Phase-4 cleanup done

### Pre-Phase-4 cleanup (2026-04-29)

End-to-end testing on the compiled exe surfaced a series of issues. All
fixed before opening Phase 4 so we don't ship Phase 4 features on a
broken base:

- **TFMs bumped 8 → 10.** Every host/lib/test csproj now targets
  `net10.0` (or `net10.0-windows10.0.19041.0`). Plugin contract surface
  (`Reader.Abstractions`, `AppModules/*`, `samples/*`, `templates/*`) stays
  on `netstandard2.1` — deliberate, plugins target the SDK.
  Side fix: `SettingsPanels.cs` aliases `DataGrid =
  System.Windows.Controls.DataGrid` to disambiguate WPF from WinForms.
- **Bare Ctrl → StopSpeech now fires.** `Win32KeyboardHook` normalizes
  L/R modifier vk codes (0xA0/0xA1, 0xA2/0xA3, 0xA4/0xA5) to generic
  forms (VK_SHIFT/VK_CONTROL/VK_MENU) before emitting RawInput.
- **Insert+Q / Insert+O / tray now pump correctly.** New `UiThread`
  owns a dedicated STA thread running `Dispatcher.Run()`; tray + dialogs
  live on it. Main thread keeps draining the speech queue.
- **SettingsWindow NPE fixed.** `_viewModel` is now built before
  `InitializeComponent()` so the ListBox's initial `SelectedIndex="0"` →
  `SelectionChanged` → `ShowCategory()` doesn't crash on a null field.
- **Tray context menu reads.** New `AnnouncingContextMenu`
  (`TrayIcon.cs`) polls `SelectedItem` while open; route through speech
  pipeline. WinForms `ToolStrip` UIA focus events are unreliable.
- **CapsLock / NumLock / ScrollLock toggles announced.** New
  `LockKeyAnnouncer` (`src/ReaderHost.Windows/LockKeyAnnouncer.cs`)
  reads `GetKeyState` after each lock-key down. Skipped when the key
  is the active Reader modifier.
- **All key echo defaults off.** `KeyboardConfig`, `KeyEchoSettings`,
  `Program.ToEchoSettings`, `SettingsViewModel` all set the four echoes
  to `false` by default. Users opt in via Settings → Keyboard.
- **`Input.ReaderModifier` config honored.** New `ResolveReaderModifier`
  takes the user's `"insert" / "capslock" / "both"` choice and falls
  back to layout default if blank.
- **God objects split.**
  - `Win32KeyboardHook` (444 lines) → 373 lines, with
    `ModifierStateTracker` + `CapsLockTapDetector` extracted.
  - `Program.cs` (652 lines) → 394 lines, with `CommandBindings`,
    `FocusContextResolver`, `UiThread`, `LockKeyAnnouncer` extracted.
  - `HumanizeCommand` (duplicated in `Program.cs` and `SettingsViewModel`)
    consolidated into `ReaderCommandLabels.Humanize` in `Reader.Input`.
- **`SpeechQueue` invariant tightened.** `DrainSignal` debug-asserts
  if the semaphore desyncs from the item count.
- **Speech preemption on focus / caret moves.** `SpeechQueue` tracks
  `_currentSpeakingGroup`; the host's drain loop sets it around each
  `engine.SpeakAsync`. When `Enqueue` arrives with a `CancelGroup` matching
  the in-flight one, `PreemptiveEnqueued` fires → engine cancels mid-utterance
  → next dequeue starts the new line. Arrowing through icons / lines no
  longer queues; it cuts.
- **Speech-rate SAPI mapping rebalanced.** Old formula saturated near 200%;
  new mapping is `step = (rate - 100) / 25` around SAPI rate 0 so the slider
  scales linearly across its whole range.
- **Dialogs steal focus reliably.** New `ForegroundWindowHelper` hooks
  `ContentRendered` (after first frame) and combines `AllowSetForegroundWindow`
  + `AttachThreadInput` + `BringWindowToTop` to defeat the foreground-lock
  policy. `DialogResult` removed from non-modal `Show()` windows — it was
  throwing `InvalidOperationException` and killing the host.
- **Insert+T reads window title, not control name.** `UiaAccessibilityProvider.GetFocusedWindowTitle`
  walks up via `TreeWalker.ControlViewWalker` to the owning `Window`.
- **Read-only edit, list/tree position, tree level, combo selection.** New
  rules in `assets/rules/defaults.yaml`. `SpeechTemplate` learned `{position}`,
  `{setSize}`, `{level}`, `{posInSet}` tokens. `UiaNodeMapper` looks up
  `PositionInSet` / `SizeOfSet` / `Level` by native UIA property id (30152 / 30153 / 30154 —
  not exposed by `System.Windows.Automation`).
- **All UIA pattern reads hardened.** Each `pattern.Current.X` read in
  `UiaNodeMapper` is in a dedicated `TryRead*` helper that swallows
  `InvalidOperationException` / `ElementNotAvailableException`. Same on
  `ReadValue`. Fixes the focus-handler crashes Cody saw in screenshots.
- **UIA mapping moved off the event thread.** Handlers now write a
  `RawUiaEvent { Kind, Element, CaretLine? }` to the channel and return.
  Mapping happens in the dispatch loop. Eliminates the 300 ms hook-cliff risk.
- **Per-focus UIA event scope.** `AttachToElement` / `DetachFromElement` swap
  per-focus subscriptions for value-changed, text-changed, and
  text-selection-changed instead of `RootElement / Subtree`. Cuts marshalling
  and removes the focus-filter check.
- **Caret-follow text reading.** New `AccessibilityEventKind.CaretMoved`
  carries the line under the caret (`AccessibilityEvent.CaretLine`); UIA's
  `TextSelectionChangedEvent` reads the line synchronously inside the handler
  (the only safe place for `TextPatternRange`). New `SpeechReason.CaretMoved`,
  `core.caret.moved` rule, cancel-group `"caret"`. Notepad-style arrow-around-text
  now reads the line at every caret move.
- **Global unhandled-exception handlers.** `Program.Main` hooks
  `AppDomain.CurrentDomain.UnhandledException` and
  `TaskScheduler.UnobservedTaskException`. `UiThread` hooks
  `Dispatcher.UnhandledException` and sets `e.Handled = true`. A future
  click-handler typo logs and survives instead of taking the host down.
- **`ConfigStore` debounce migrated to `Timer.Change`.** No more
  `Thread.Sleep(50)` blocking thread-pool workers on rapid file-save bursts.
- **`PluginManifestFile.Capabilities` declarative field.** Open string set
  (`accessibility-read`, `audio-output`, `network-out:host:port`, etc.).
  Advisory only today — `PluginHost` logs declared capabilities on load.
  Phase 4d will turn it into host-enforced grants without re-publishing.

Test count now: **162 passed, 2 skipped, 0 failed**. New test classes:
`KeyEchoSettingsDefaultsTests`, `KeyboardConfigDefaultsTests`,
`ReaderCommandLabelsTests`, `GestureMapTests.Bare_Ctrl_resolves_to_StopSpeech`,
`SpeechQueueTests` (cancel-group preemption regressions),
`PositionAndLevelTests` (list/tree/edit/caret/combo rules).

```
dotnet build AURA.slnx     →  Build succeeded.   0 Warning(s)   0 Error(s)
dotnet test  AURA.slnx     →  162 passed, 2 skipped, 0 failed
dotnet run   --project src/ReaderHost.Windows
  → tray icon appears with right-click menu (items announce on hover/arrow)
  → "Aura ready" announcement
  → first-party app modules load (Browser, Explorer, VS Code, Notepad++)
  → Insert+O opens Settings with focus, Insert+Q opens Exit dialog with focus
  → arrow through Notepad / a read-only chat history reads each line as the caret moves
```

Phase-2 and Phase-3 punch lists are closed. See `docs/ROADMAP.md` for the
checked-off items.

### Phase 2 final additions (this session)

| Item | Status |
|---|---|
| `ConfigStore.RemoveLayer` / `InsertFileLayer` | ✅ |
| Profile-layer hot-swap in `Program.cs` Changed handler | ✅ — drops old + inserts new before `app` layer |
| `KeyTranslator` honours OS CapsLock toggle | ✅ — reads `GetKeyState(VK_CAPITAL)`; testable via explicit override |
| `ReaderCommand.SayAll` (start-from-top) | ✅ — `SayAllRunner.StartFromBeginningAsync` |
| WiX 4 MSI installer (`installer/`) | ✅ — opt-in build, wired into `release.yml` |

### Phase 3 — built this session

**Plugin host** (`Reader.Scripting/`):

- `PluginApi` — host's `CurrentApiVersion = 1.0`; `IsCompatible(Version)`
  enforces same-major + minor-≤-host.
- `PluginManifestFile` — JSON schema for `manifest.json`. `TryLoad` validates
  required fields and parseable versions.
- `PluginLoadContext` — collectible ALC; defers `Aura.Abstractions`,
  `Aura.Diagnostics`, and `Serilog` to the host's default ALC so
  contract types match.
- `PluginContext` — per-attach `IAppContext`; tracks rules registered via
  `RegisterSpeechRule` and disposes them on detach.
- `PluginHost` — orchestrator. Multiple roots; per-root watcher in dev;
  `LoadAllAsync`, `OnFocusChangedAsync`, `ReloadAsync`, `DisposeAsync`.
  Raises `RulesChanged` whenever the plugin rule set changes.
- `PluginPaths` — `UserPluginsRoot`, `ShippedAppModulesRoot`.

**SpeechPipeline.UpdateRuleEngine** — atomic swap of the rule engine. The
host wires `RulesChanged` → rebuild engine from base + plugin rules → swap.

**First-party app modules** (`src/AppModules/`):

- `AppModule.Browser` — Edge / Chrome / Brave (tab + address-bar).
- `AppModule.Explorer` — explorer.exe selection enrichment.
- `AppModule.VsCode` — code.exe / code-insiders.exe; suppresses noisy
  status-bar focus, customises editor announcement.
- `AppModule.NotepadPlusPlus` — notepad++.exe tab announcement.

Each is `netstandard2.1`, references only `Reader.Abstractions`. Built
into `<host>/bin/.../app-modules/<id>/` by a `CopyAppModules` MSBuild
target in `ReaderHost.Windows.csproj`.

**SDK NuGet** — `Reader.Abstractions.csproj` is now packable as
`Aura.Sdk` (PackageId distinct from AssemblyName). XML docs included.
Pushed to NuGet by `release.yml`'s `sdk` job on tag.

**Sample plugin + template**:

- `samples/SamplePlugin` — 30-line "tab changed in Edge" announcer; the
  Phase-3 acceptance criterion proof point.
- `templates/aura-plugin` — `dotnet new` template scaffolding a
  plugin csproj + manifest + module.
- `templates/AURA.Templates.csproj` — packs the template as a NuGet
  template package.

### Test count this session

```
Reader.Abstractions.Tests   2
Reader.Config.Tests        22  (+3: KeyboardConfigDefaults; +1 word-echo defaults split)
Reader.Core.Tests          16  (+4: SayAllRunner)
Reader.Diagnostics.Tests    2
Reader.Input.Tests         59  (+7: Bare_Ctrl + KeyEchoDefaults + ReaderCommandLabels)
Reader.Output.Tests         0  (1 skipped placeholder)
ReaderPlatform.Windows.Tests 0 (1 skipped placeholder)
Reader.Scripting.Tests     27  (PluginApi/Manifest/Host/FirstParty)
Reader.Speech.Tests        36  (+8: SpeechQueue cancel-group preempt + PositionAndLevel)
                          ───
                          162 passed, 2 skipped, 0 failed
```

## Plan — what's next (Phase 4: depth)

Per `docs/ROADMAP.md`, now structured into nine sub-phases. User-validated
order:

1. **4a — eSpeak-NG engine.** Already-extant `ISpeechEngine` contract.
2. **4b — Audio themes** (was "earcons"). Disabled by default; default
   `SineBellTheme` (decaying sine bursts, per-role frequencies — sines not
   squares). Sound-pack support.
3. **4c — Browse / Focus mode.** Killer feature for web; treat as its own
   mini-roadmap (~6–10 weeks).
4. **4d — Plugin contract widening.** `IPluginCommand`, `IAudioTheme`,
   `ISettingsPanel`; promote `ISpeechEngine` and `IInputSource` to plugin
   contracts; lifecycle hooks; manifest capability enforcement (the
   `Capabilities` field already exists, advisory-only). API v1.1.
5. **4e — OCR.** `Windows.Media.Ocr`.
6. **4f — Update mechanism.** After 2–3 trusted MSI releases.
7. **4g — Braille.** Defer until a user with a specific display asks.
8. **4h — Display-model hooking.** Defer indefinitely.
9. **4i — Remote relay.** Recommended robust architecture: Shape B (UIA
   event mirroring over WebSocket + TLS + PSK auth) shipped as
   `Aura.Relay.{Server,Client}` plugins. Slot **after 4d** so it
   ships as plugins, not a fork.

### Original Phase 4 list (now split across 4a–4i above)

The bullets below are kept for historical context; treat the sub-phase
ordering as authoritative.

1. **eSpeak-NG engine** — P/Invoke wrapper, voice management. Multi-engine
   arbitration. The contract is already in `Reader.Abstractions`
   (`ISpeechEngine`).
2. **Earcons** — audio mixer, configurable sound packs.
3. **Audio ducking** — Windows `IAudioVolumeDuck`.
4. **Browse mode / virtual buffer** — hardest single feature. Start with
   one browser via UIA only.
5. **OCR** — `Windows.Media.Ocr` fallback for image content.
6. **Update mechanism** — Squirrel or velopack. After we have a release
   we trust.

Phase 4 has no fixed deadline; users will tell us what to build by then.

## Open notes / nits

1. **Plugin DLL file lock.** On Windows, an ALC-loaded DLL can't be
   deleted while the ALC is alive. `ReloadAsync` unloads the ALC, but the
   file may stay locked until GC collects. Reloading the same plugin
   in-place works fine. Removing a plugin at runtime is supported via
   manifest deletion (DLL stays until process exit). Documented in
   `ARCHITECTURE.md`.
2. **Profile-layer change requires no restart any more** — wired this
   session.
3. **CapsLock toggle in KeyTranslator** — fixed; reads `GetKeyState`.
4. **Single-instance lock** still leaks on hard kill. Same as before.
5. **First-party modules' rules are basic.** Each registers 1-2 rules
   that demonstrate the contract. Users expect more for real use; these
   are scaffolds, not finished integrations. Phase 4 deepens them.
6. **No publishing pipeline test.** `release.yml` is wired but unproven —
   the first tag we cut will exercise it. The script
   `scripts/regenerate-installer-files.ps1` produces a deterministic file
   list; the WiX build harvests from `publish/host`.
7. **Plugin capabilities are advisory.** `PluginManifestFile.Capabilities`
   is logged at load time but not enforced. Phase 4d will turn declarations
   into host-enforced grants without re-publishing.
8. **Audit items deferred.** A7 (rule-engine debug log on skipped Rewrites),
   A9 (KeyEcho IME edge case — speculative), A10 (UiThread.Dispose join
   timeout), A11 (`_open` field locking on dialog hosts — UI thread is
   single-threaded so safe in practice), A12 (Plugin DLL file lock — see
   note 1 above), A13 (KeyChordParser allowing redundant modifier aliases),
   A14 (Win32 hook drops events on TryWrite failure during shutdown — only
   fires after Writer.TryComplete()).

## Useful commands

```
cd "/run/media/cody/Personal Data/Data/Github/OpenReader"
dotnet build AURA.slnx -p:EnableWindowsTargeting=true
dotnet test  AURA.slnx --no-build
dotnet run   --project src/ReaderHost.Windows

# enable plugin hot-reload
$env:AURA_DEV = "1"
dotnet run --project src/ReaderHost.Windows

# pack the SDK
dotnet pack src/Reader.Abstractions -c Release -o ./packs

# pack the template
dotnet pack templates/AURA.Templates.csproj -c Release -o ./packs

# build the MSI (requires `dotnet tool install --global wix --version 4.0.5`)
dotnet publish src/ReaderHost.Windows -c Release -r win-x64 `
    --self-contained false -o publish/host
pwsh scripts/regenerate-installer-files.ps1
dotnet build installer/AURA.Installer.wixproj -c Release -p:ProductVersion=0.1.0
```

User config: `%AppData%\Aura\config.json`
User plugins: `%AppData%\Aura\plugins\<id>\`
First-party app modules: `<host>\app-modules\<id>\`
Logs: `%LocalAppData%\Aura\logs\aura-<yyyyMMdd>.log`
