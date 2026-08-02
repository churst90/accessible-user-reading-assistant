# Session Handoff

Last updated: 2026-07-30

This is the load-on-startup brief for whoever picks up next. Read it before
opening anything else.

## Where we are: architectural pass done; Phase 3.6 mostly closed

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
cd F:/data/Github/Aura
dotnet build AURA.slnx
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
