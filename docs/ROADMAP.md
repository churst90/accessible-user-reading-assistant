# Roadmap

Each phase ends with something demonstrable. No phase is "internal cleanup."

## Phase 0 — Foundation (target: 2 weeks)

Outcome: empty solution builds, CI is green, the abstractions compile, the
test harness runs against a synthetic provider.

- [ ] `Aura.sln` with all 10 projects scaffolded, no implementation.
- [ ] `Reader.Abstractions` defines: `IAccessibilityProvider`, `AccessibleNode`,
      `AccessibleRole`, `AccessibilityEvent`, `ISpeechEngine`,
      `SpeechUtterance`, `IInputSource`, `RawInput`, `IAppModule`,
      `AppModuleManifest`.
- [ ] `Reader.Diagnostics` set up with Serilog, rotating file sink.
- [ ] xUnit projects for every src project. CI runs them.
- [ ] GitHub Actions workflow: build + test on `windows-latest`.
- [ ] `.editorconfig`, `Directory.Build.props` enforcing nullable,
      analyzers, warnings-as-errors.
- [ ] `SyntheticAccessibilityProvider` in `tests/` — fluent builder for
      fake trees, used by all upstream unit tests.

**Done when:** `dotnet test` passes on a fresh clone and shows >0 tests.

## Phase 1 — Vertical slice (target: 4 weeks)

Outcome: launch the host, focus a control in Notepad, hear it announced
through SAPI5. Nothing else works.

- [ ] `ReaderPlatform.Windows.UIA`: subscribe to `FocusChanged` UIA events,
      translate to `AccessibilityEvent`.
- [ ] `Reader.Core` event loop: receive events, hand to speech.
- [ ] `Reader.Speech`: minimal rule engine with built-in defaults for
      `Button`, `Edit`, `MenuItem`, `ListItem`, `CheckBox`. Rule format
      lives in YAML files loaded from `assets/rules/`.
- [ ] `ReaderPlatform.Windows.Sapi5`: `ISpeechEngine` adapter over SAPI5.
- [ ] `ReaderHost.Windows`: console exe (no tray yet). Wires everything,
      runs until Ctrl+C.
- [ ] Single-instance lock so two hosts can't both grab focus events.

**Done when:** `dotnet run --project ReaderHost.Windows`, then opening
Notepad and clicking File → Open speaks "File menu" then "Open menu item."

## Phase 2 — Usability (target: 6 weeks) — **closed 2026-04-28**

Outcome: someone could plausibly use it for simple tasks.

- [x] `Reader.Input`: low-level keyboard hook, configurable modifier
      ("Insert" key), gesture model, command map.
- [x] Built-in commands: stop speech, repeat last, read current line,
      review cursor (next/prev character/word/line), focus follows review.
- [x] Tray icon with status indicator and menu (settings, exit).
- [x] Settings UI — WPF, lives in `ReaderUI.Windows`. Panels for General,
      Speech, Keyboard, Key bindings.
- [x] `Reader.Config` persists user settings; reload on file change.
      Layered (defaults / machine / user / profile / app), profile
      hot-swap, app-specific overrides.
- [x] MSI installer (WiX 4) — `installer/`, built by
      `.github/workflows/release.yml` on tag.
- [x] `SayAll` from focus root, in addition to `SayAllFromCursor`.
- [x] CapsLock toggle state honoured by key-echo translator.

**Done when:** a user can install it, open Notepad, type, navigate by
arrow keys and Tab, hear everything, and exit cleanly. ✅

## Phase 3 — Extensibility (target: 6 weeks) — **closed 2026-04-28**

Outcome: third parties can ship app modules without our help.

- [x] `Reader.Scripting` plugin host: collectible `AssemblyLoadContext`,
      manifest validation (JSON schema + API-version gate),
      contract-versioned interface surface (`PluginApi.CurrentApiVersion`).
- [x] First-party app modules for: Browser (Edge/Chrome/Brave),
      Explorer, VS Code, Notepad++. Validates the contract by *being our
      own first consumer* — they go through the same loader as third-party
      plugins, into per-plugin ALCs, with manifest-declared API versions.
- [x] Hot-reload in dev mode (gated by `AURA_DEV` env var).
- [x] Public API XML doc comments enabled on the SDK assembly; flow into
      the NuGet package's `lib/.../Aura.Abstractions.xml`.
- [x] `Aura.Sdk` NuGet package — `dotnet pack
      src/Reader.Abstractions` produces `Aura.Sdk.<version>.nupkg`.
- [x] In-tree sample plugin (`samples/SamplePlugin`) and `dotnet new`
      template (`templates/aura-plugin`, packed as
      `Aura.Templates`).

**Done when:** an external developer can `dotnet new` a template, write a
30-line shim that announces "tab changed in Edge," and load it without
recompiling Aura. ✅ — see `samples/SamplePlugin/TabAnnouncerModule.cs`.

## Phase 3.5 — Pre-Phase-4 cleanup — **closed 2026-04-29**

End-to-end testing of the compiled exe surfaced a long list of issues
the unit-test pyramid couldn't catch. All fixed before opening Phase 4
so we don't ship Phase 4 features on a broken base. Closed with
**162 passed, 2 skipped, 0 failed**.

### Foundation

- [x] Bump every TFM from `net8.0` to `net10.0` (plugin contract surface
      stays `netstandard2.1`).
- [x] Normalize L/R modifier vk codes in `Win32KeyboardHook` so bare-Ctrl
      → StopSpeech actually resolves.
- [x] Move tray + dialogs to a dedicated STA `UiThread` running
      `Dispatcher.Run()` so Insert+O / Insert+Q dialogs actually appear.
- [x] Honor `InputConfig.ReaderModifier` config (was being ignored).
- [x] Speech-rate SAPI mapping rebalanced (was saturating at ~200%).

### Dialogs

- [x] `SettingsWindow` NPE — `_viewModel` built before `InitializeComponent`
      AND `SelectionChanged` handler null-guards XAML named fields (which
      aren't assigned during `EndInit`).
- [x] `DialogResult` removed from non-modal `Show()` windows — was throwing
      `InvalidOperationException`.
- [x] `ForegroundWindowHelper` with `ContentRendered` hook +
      `AllowSetForegroundWindow` + `AttachThreadInput` for reliable focus.
- [x] Tray context-menu narration via `AnnouncingContextMenu`.
- [x] `LockKeyAnnouncer` for CapsLock / NumLock / ScrollLock toggles.
- [x] `Insert+T` walks up to the owning Window (was reading focused control name).

### Speech

- [x] Speech preemption: `SpeechQueue._currentSpeakingGroup` + cancel-group
      match → `PreemptiveEnqueued`. Focus / caret moves cut in-flight speech
      instead of queueing.
- [x] `core.role.edit.readonly` rule (priority 20).
- [x] `core.role.treeitem` template includes `{level}` and `{posInSet}`.
- [x] `core.role.listitem` template includes `{posInSet}`.
- [x] `core.combobox.value_changed` rule for selection changes.
- [x] `SpeechTemplate` learned `{position}`, `{setSize}`, `{level}`,
      `{posInSet}` tokens.
- [x] `UiaNodeMapper` looks up `PositionInSet` / `SizeOfSet` / `Level` by
      native UIA property id (30152 / 30153 / 30154).
- [x] All UIA pattern reads wrapped against `InvalidOperationException`.

### Code-base audit (A1–A8)

- [x] **A1 — Caret-follow text reading.** New `AccessibilityEventKind.CaretMoved`
      + `SpeechReason.CaretMoved` + `core.caret.moved` rule. Notepad-style
      arrow-around-text reads each line as the caret moves. Cancel-group
      `"caret"` so rapid arrow presses preempt.
- [x] **A2 — UIA mapping moved off the event thread.** Handlers write
      `RawUiaEvent { Kind, Element, CaretLine? }` to the channel and return.
      Mapping happens on the consumer thread. Eliminates the 300 ms hook-cliff.
- [x] **A3 — Global unhandled-exception hooks.** `AppDomain.UnhandledException`,
      `TaskScheduler.UnobservedTaskException`, `Dispatcher.UnhandledException`
      (`e.Handled = true`).
- [x] **A4 — Per-focus UIA event scope.** `AttachToElement` /
      `DetachFromElement` swap value-changed / text-changed / text-selection
      subscriptions per focus. Cuts marshalling cost.
- [x] **A5 — Tests for new tokens & rules.** `PositionAndLevelTests` (6 cases).
- [x] **A6 — `Timer.Change` debounce in `ConfigStore`.** Removes the
      thread-pool-blocking `Thread.Sleep(50)`.
- [x] **A8 — `PluginManifestFile.Capabilities` declarative field.**
      Open string set; advisory only today; logged at plugin load.
      Phase 4d enforces.

### God-object splits

- [x] `Win32KeyboardHook` (444 lines) → 373 lines, with
      `ModifierStateTracker` + `CapsLockTapDetector` extracted.
- [x] `Program.cs` (652 lines) → 394 lines, with `CommandBindings`,
      `FocusContextResolver`, `UiThread`, `LockKeyAnnouncer`,
      `ForegroundWindowHelper` extracted.
- [x] `HumanizeCommand` consolidated into `ReaderCommandLabels.Humanize`
      in `Reader.Input` (was duplicated in `Program.cs` and
      `SettingsViewModel`).

## Phase 3.6 — Core correctness pass — **opened 2026-05-27, mostly closed 2026-07-30**

Phase 4a (eSpeak NG engine) shipped 2026-05-08. Before continuing into
Phase 4 features (audio themes / Piper neural TTS / RVC), the user halted
feature work to fix core reading / echo / interrupt correctness. The bar is
**verified by running the compiled exe** (Notepad, Win+R Run box, arrowing,
dialogs), not just unit tests. Ordered by user-visible impact:

- [x] **#1 — Edit / document focus reads the whole buffer.** `core.role.edit`
      and `core.role.edit.readonly` emit `{value}`, which is the *entire*
      `ValuePattern` text (`UiaNodeMapper.ReadValue`). Focusing a multi-line
      edit dumps the whole document. Fix: capture the caret line on focus and
      announce that (single-line edits read their full value unchanged;
      multi-line read only the current line — NVDA behavior). Password role
      stays excluded.
- [x] **#2 — Selection / alert / window-open events are dead.** Partly done:
      `UiaAccessibilityProvider` now registers desktop-wide handlers for
      `WindowOpened`, `SelectionItem.ElementSelected`, `MenuOpened` and
      `ToolTipOpened`, with matching rules in `defaults.yaml`. The root cause
      was not just missing registrations — every subscription was scoped to
      the *focused* element, and alerts fire on elements that by definition
      are not focused. **Still missing:** live regions and notifications
      (`UIA_LiveRegionChangedEventId` 20024, `UIA_NotificationEventId` 20023)
      have no managed-API equivalent and need the native COM migration.
      `SpeechPipeline` subscribes to `SelectionChanged | AlertRaised |
      LiveRegionChanged` but `UiaAccessibilityProvider` only ever raises
      Focus / Value / Caret. No `WindowOpenedEvent` or foreground hook either.
      Result: list/grid selection (that uses UIA selection events, not
      value-changed), toasts/alerts, web live regions, and non-focus-stealing
      dialogs are silent. Wire the missing UIA event registrations.
- [x] **#3 — Review cursor doesn't track the system caret.** `ReviewCursor`
      is now an `ITextRange` over the same surface the caret uses, so
      `FollowCaret()` is a position copy. Wired to `CaretMoved` in the host. `ReviewCursor`
      only re-syncs on focus change; Reader+arrow reviews from a stale offset.
      Make review loosely follow the caret (NVDA default).
- [x] **#4 — Caret-follow racing a fixed 15 ms timer.** Replaced wholesale
      rather than tuned: `CaretTracker` + `CaretMotionResolver` compare
      observed positions, so the keystroke and the UIA event are
      interchangeable triggers and neither suppresses the other. All four
      timing constants are gone. Old text below kept for context. `CaretLineTracker`
      schedules `Task.Run` + `Task.Delay(15ms)` per keystroke and reads the
      caret; fragile under load, and fires unsynchronized concurrent UIA reads
      on rapid arrowing. Make it event-driven (trust UIA
      `TextSelectionChangedEvent` where supported; keystroke + Win32 only as
      the fallback for controls that don't fire it, e.g. classic Notepad).
- [ ] **#5 — Nits.** Word echo splitting on apostrophe/hyphen is fixed for
      the text model (a word is a run of non-whitespace, covered by
      `StringTextSurfaceTests`), but `KeyEchoService` has its own splitter and
      still needs porting. `KeyTranslator` dead-key reset is still documented
      but not performed. Char echo cut by the generic cancel on fast typing is
      unverified.
- [ ] **#6 — Backspace / Delete announcement.** Regressed by the #4 rewrite
      and deliberately left out of it: position diffing cannot describe an
      edit, because the two positions belong to different documents. Saying
      what was just deleted needs the text captured *before* the keystroke,
      which belongs in key echo. `CaretFollowService.MightMoveCaret` excludes
      both keys so nothing wrong is announced in the meantime.
- [x] **Refactor (from the architecture question):** done as
      `ITextSurface` / `ITextRange` / `ITextSurfaceProvider` — see
      `docs/TEXT_MODEL.md`. Original note below.
      strategy so the Win32 `EM_*` / `WM_GETTEXT` fallback stops being
      hard-wired inside the UIA `CaretLineTracker` / `UiaTextContentProvider`.
      Keep the two existing seams (`IAccessibilityProvider` for the whole
      backend; app-modules for per-app). Do **not** split providers per
      windowing library — UIA already unifies Win32/WPF/UWP.

**Done when:** focusing Notepad / the Run box reads sensibly, arrowing reads
the right line every time, selection/alert/dialog events are heard, and review
navigation tracks the caret — all confirmed on the running exe.

## Phase 4 — Depth (target: open-ended)

Ordered by user-visible impact and dependency. Items toward the bottom
depend on contract surface added by items above them.

### 4a — Speech engines and voice options

- [x] **eSpeak-NG engine.** P/Invoke over `libespeak-ng.dll`,
      `EspeakNgEngine : ISpeechEngine` alongside `Sapi5Engine`. Voices
      enumerated from `espeak_ListVoices`. Prosody mapped (rate WPM,
      volume 0–200, pitch 0–100). Cancellation via `espeak_Cancel` from
      another thread. New `EngineRouter` in `Reader.Speech` lets the
      synth-selection dialog hot-swap without restart. **Requires user
      to install eSpeak NG** ([release page](https://github.com/espeak-ng/espeak-ng/releases));
      `DllNotFoundException` at engine construction is caught and the
      host falls back to SAPI 5 only. Bundling the binary + data files
      into the MSI installer is a follow-up.
- [ ] **Per-language fallback / per-app preference.** Future work when
      multi-engine voice arbitration matters.

### 4b — Audio themes (was "earcons" — broader scope)

- [ ] **`IAudioTheme` plugin contract** in `Reader.Abstractions`. Cue
      kinds (`FocusButton`, `FocusCheckbox`, `Selected`, `LineEnd`,
      `Loaded`, `Saved`, `Error`, etc.) — open enum, well-known set.
- [ ] **First-party `SineBellTheme`** — short decaying sine bursts
      (`A·exp(-t/τ)·sin(2πft)`), per-role frequency mapping. Easier on
      the ears than NVDA's square-wave earcons.
- [ ] **Audio mixer** (NAudio or XAudio2) so themes layer under speech.
- [ ] **Audio ducking** — Windows `IAudioVolumeDuck` to lower other apps
      during speech. Off by default.
- [ ] **Sound packs** — themes load from `%AppData%\Aura\themes\<id>\`
      with `theme.json` mapping `CueKind → wav/oga`.
- [ ] **Settings panel: Audio.** Theme dropdown, volume, "Test theme"
      button playing one cue per role. Disabled by default.

### 4c — Browse / Focus mode (its own mini-roadmap)

The killer web-browsing feature; deserves its own dedicated work.
~6–10 weeks total. **User-confirmed priority (2026-05-27):** "read and
navigate web views and have web elements read appropriately." Depends on the
Phase 3.6 #2 work (live-region / selection / structure events must fire before
web content reads correctly) — do the correctness pass first.

- [ ] **`IBrowseSurface` plugin contract.** UIA-backed browser for v1
      (Edge/Chrome/Brave).
- [ ] **`VirtualBuffer`** — flat reading representation built from the
      focused document's UIA tree. Quick-keys (`h` next heading, `H`
      prev, `k` link, `e` edit, `b` button, `f` form field, etc.).
- [ ] **Focus mode auto-detection** — caret enters editable / form
      control → switch off browse mode. Insert+Space toggles manually.
- [ ] **Per-element renderers** — heading levels, link targets, form
      labels, table semantics.
- [ ] **Per-site overrides** — "always Focus on x.com", "never browse
      mode in y.app". Persisted in profile.

### 4d — Plugin contract widening (foundation for 4e+)

Today plugins can ship `SpeechRule`s and react to focus changes. To
unlock the relay, audio themes, OCR plugins, and richer addons, expose:

- [ ] **`IPluginCommand`.** Plugin declares `Id`, `DefaultChord`,
      `Handler`. Host wires it into the gesture map and rebind UI.
      Required before audio-themes or browse-mode plugins are useful.
- [ ] **`IAudioTheme`** (see 4b).
- [ ] **`ISettingsPanel`.** Plugin contributes a category to the
      Settings dialog with a view-model + view factory.
- [ ] **Promote `ISpeechEngine` to plugin contract.** Already exists
      internally; move to `Reader.Abstractions`. Allows third-party
      engines (Azure, ElevenLabs, the relay) without forking.
- [ ] **Promote `IInputSource` to plugin contract.** Allows braille,
      touch, network-input plugins.
- [ ] **Lifecycle hooks.** `OnStartup`, `OnShutdown`, `OnProfileChanged`,
      `OnEnabledChanged`.
- [ ] **Manifest capability declarations.** `"capabilities":
      ["accessibility-read", "audio-output", "network-out:127.0.0.1:*"]`
      with a UI prompt when a plugin asks for something sensitive.
      **These are declarations of intent, not enforced grants.** In-process
      plugins run at full trust; an ALC is a type-identity boundary, not a
      security one, and nothing stops a plugin from ignoring its own
      manifest. Genuine enforcement needs a process boundary per plugin —
      scope that separately and price the latency before promising it.
      Until then the honest position is "install only what you trust".
- [ ] **Manifest signing.** Authenticode or detached signature. Optional
      today, required in unattended install scenarios.
- [ ] Bump `PluginApi.CurrentApiVersion` to 1.1; v1.0 plugins keep
      working.

### 4e — OCR

- [ ] **`Windows.Media.Ocr` integration** — fallback to OCR when a
      focused element has no text but has an image. Auto-trigger
      threshold configurable. Manual chord (`Insert+I`) to OCR the
      focused element on demand. ~1 week.

### 4f — Update mechanism

- [ ] **Velopack or Squirrel** auto-updater pulling from a GitHub
      release feed. Wait until 2–3 trusted manual MSI releases first —
      auto-update is only useful when the release process is solid.

### 4g — Braille (deferred until a user asks)

- [ ] **liblouis P/Invoke** for translation.
- [ ] **`IBrailleDisplay` plugin contract.** Per-display drivers via
      the plugin model.
- [ ] **One reference driver** for a display we can physically test
      against. Community owns the rest.

### 4h — Display-model hooking (`native/win-injector`) — defer

Only if a named user-requested app forces it (some old MFC, custom
terminals, legacy Java AWT). UIA covers ~95%; this is the long tail.
Code-signing requirements and AV/Defender false-positives make this
expensive to ship.

### 4i — Remote relay (its own phase, after 4d)

Recommended shape: **Shape B (command relay) — UIA event mirroring**.
Lower bandwidth than audio streaming, voice/prosody preferences stay
local, latency-tolerant.

- [ ] **`Aura.Relay.Server` plugin** — exposes
      `IAccessibilityProvider` + `IInputSource` over WebSocket. Runs
      on the remote machine.
- [ ] **`Aura.Relay.Client` plugin** — mirror provider/input on
      the local machine. Suppresses the local accessibility provider
      while connected. The local user's voice + prosody + audio theme
      are used.
- [ ] **TLS + pre-shared-key auth.** Optional Windows-domain Kerberos.
- [ ] **Capability-gated network access** (uses 4d capability system).
- [ ] **`Aura.Relay.Server.Web`** (optional) — read-only HTML
      viewer for QA / demos / support tickets. SSE event stream + MP3
      chunks of synthesized speech.

Built **as plugins**, not host-baked, so the contract widening from
4d gets battle-tested at the same time. ~3 weeks for Shape B once 4d
is in place; +1 week for the web viewer.

**Done when:** Phase 4 has its own roadmap and we're picking work from
user telemetry, not guesses.

## Phase 5 — Linux (target: not until Phase 4 stabilizes)

Outcome: a usable second platform, demonstrates the abstraction held.

- [ ] `ReaderPlatform.Linux.AtSpi`: D-Bus client, `IAccessibilityProvider`
      implementation.
- [ ] `Reader.Speech.Speechd` engine adapter (speech-dispatcher is the
      Linux norm).
- [ ] `ReaderHost.Linux`: systemd user service, GNOME-first.
- [ ] Wayland input: cooperate with compositor where possible, document
      what doesn't work.

**Done when:** can replace Orca for a specific user's daily flow on GNOME.

## What we are deliberately not doing

These come up; the answer is "later, if at all":

- Mobile (iOS/Android already covered well).
- Mac (VoiceOver covered well).
- Web-based screen reader.
- Cloud-synced settings.
- AI-generated content descriptions (interesting, but a separate product).
- A "lite" mode that competes with Narrator. Narrator is fine; users
  switching to us want depth.
