# Architecture

## Layering

```
+-----------------------------------------------------------+
|  ReaderHost.Windows           (exe, tray, lifecycle)      |
+-----------------------------------------------------------+
|  ReaderPlatform.Windows       (UIA, MSAA, IA2, Win32)     |
+-----------------------------------------------------------+
|  Reader.Core                  (event loop, focus, cursor) |
|  Reader.Speech / Output / Input / Scripting / Config      |
+-----------------------------------------------------------+
|  Reader.Abstractions          (no platform types leak up) |
+-----------------------------------------------------------+
|  native/win-injector          (C++ shim — only when needed)|
+-----------------------------------------------------------+
```

Strict rule: **Core depends only on Abstractions. Platform implements
Abstractions. Host wires them together.** A future `ReaderPlatform.Linux`
plugs in at the same seam.

## Projects

| Project                       | TFM              | Purpose |
|------------------------------|------------------|---------|
| `Reader.Abstractions`        | `net8.0`         | Interfaces, value types. No dependencies. The plugin contract. |
| `Reader.Core`                | `net10.0`        | Event loop, focus tracker, text model, review cursor, command dispatcher. |
| `Reader.Speech`              | `net10.0`        | Speech queue, rule pipeline, engine adapters. |
| `Reader.Output`              | `net10.0`        | Arbitrates speech vs. braille vs. earcons. |
| `Reader.Input`               | `net10.0`        | Gesture model, key map, command resolution. |
| `Reader.Scripting`           | `net10.0`        | Plugin host. Loads, isolates, versions. |
| `Reader.Config`              | `net10.0`        | Layered config, profile resolution, persistence. |
| `Reader.Diagnostics`         | `net10.0`        | Logging, redaction, metrics, perf counters. |
| `ReaderPlatform.Windows`     | `net10.0-windows`| UIA, Win32, WinEvents, SAPI5, eSpeak NG. |
| `ReaderHost.Windows`         | `net10.0-windows`| Exe entry point, tray icon, single-instance, updates. |

**Why the contract is `net8.0` and everything else is `net10.0`.** Plugins load
into the host's process on the host's runtime, so the contract never needs to
run anywhere the host doesn't. Pinning it one LTS behind the host means the
host can move forward without forcing every plugin author to retarget. It is
deliberately *not* `netstandard2.1` — that bought nothing (no plugin will ever
run on .NET Framework) and cost a hand-written `IsExternalInit` polyfill just
to make `record` compile.

**NativeAOT is not a goal.** It cannot coexist with the plugin system:
`PluginHost` loads assemblies at runtime into a collectible
`AssemblyLoadContext` and activates types through `Activator.CreateInstance`,
which AOT does not support. As long as C# app modules exist — and they are the
whole of Phase 3 — AOT is unreachable, and pretending otherwise distorts
unrelated decisions. `PublishReadyToRun` and `TieredPGO` deliver the startup
and jitter wins that were actually wanted. See `ASSESSMENT.md` S8.

## Key abstractions

These live in `Reader.Abstractions`. Names are sketches; signatures will refine.

### `IAccessibilityProvider`
The single seam between platform and core. Core never sees a UIA element.

```csharp
public interface IAccessibilityProvider : IAsyncDisposable
{
    AccessibleNode? Focused { get; }
    AccessibleNode? Root { get; }
    AccessibleNode? FromPoint(int x, int y);
    IDisposable Subscribe(AccessibilityEventKind kinds, Action<AccessibilityEvent> handler);
}
```

### `AccessibleNode`
Neutral, immutable snapshot. Lazy children. No platform handles leak.

```csharp
public sealed record AccessibleNode(
    NodeId Id,
    AccessibleRole Role,
    string? Name,
    string? Value,
    string? Description,
    AccessibleStates States,
    NodeId? ParentId,
    Func<IReadOnlyList<AccessibleNode>> ChildrenFactory,
    IReadOnlyDictionary<string, object?> Extras);
```

`Extras` is the escape hatch for platform-specific data app shims may want
(e.g. UIA `AutomationId`). Discouraged for core code; necessary for shims.

### `AccessibleRole`
A normalized enum that maps from UIA `ControlType` and AT-SPI `Role`. ARIA
proved this normalization layer is workable.

### `AccessibilityEvent`
```csharp
public sealed record AccessibilityEvent(
    AccessibilityEventKind Kind,
    AccessibleNode? Node,
    DateTimeOffset At);
```

### `ISpeechEngine`
```csharp
public interface ISpeechEngine : IAsyncDisposable
{
    ValueTask SpeakAsync(SpeechUtterance utterance, CancellationToken ct);
    ValueTask CancelAsync();
    IReadOnlyList<VoiceInfo> Voices { get; }
}
```
Engine is dumb. The speech *queue* and rule pipeline live in `Reader.Speech`.

### `IInputSource`
```csharp
public interface IInputSource : IAsyncDisposable
{
    event EventHandler<RawInput> RawInputReceived;
}
```
Raw keyboard / braille input enters here. The gesture layer in `Reader.Input`
maps it to high-level commands.

### `IAppModule` (the plugin contract)
```csharp
public interface IAppModule
{
    AppModuleManifest Manifest { get; }
    bool Matches(ProcessInfo process);
    ValueTask OnAttachAsync(IAppContext ctx);
    ValueTask OnDetachAsync();
}
```
`AppModuleManifest` declares the API version it targets; the host refuses to
load incompatible modules.

## Threading and async

- One **event dispatch thread** owns focus state. Platform raises events to it
  via a `Channel<AccessibilityEvent>`.
- Speech runs on a dedicated thread with its own queue. Cancellable on every
  utterance.
- Heavy work (tree walks, regex compilation) goes to the thread pool. Results
  flow back to the dispatch thread via channels.
- No `lock` on the hot path. Use immutable snapshots (`record`s) and channels.

This eliminates the "what thread am I on?" question that haunts NVDA's Python
threading model.

## Speech pipeline (overview, full detail in `SPEECH_PIPELINE.md`)

```
AccessibleNode + EventKind
       │
       ▼
[ SpeechRuleEngine ]   ← layered config (defaults, user, app, script)
       │
       ▼
SpeechUtterance (text + prosody hints + voice hint)
       │
       ▼
[ SpeechQueue ]   ← arbitration with braille / earcons
       │
       ▼
ISpeechEngine.SpeakAsync
```

The rule engine is **data-driven**. Built-in rules live in JSON/YAML. Users
edit them in a settings UI. Scripts contribute additional rules. The same
pipeline serves "what to say when focus moves" and "what to say when reading
this character."

## Configuration

Layers, resolved last-wins:
1. **Built-in defaults** (shipped with binary)
2. **Machine** (`%ProgramData%\OpenReader\config.json`)
3. **User** (`%AppData%\OpenReader\config.json`)
4. **Profile** (active named profile)
5. **App-specific override** (per executable name)
6. **Script-contributed** (runtime, marked as such)

Config is a typed POCO graph, JSON-serialized. Reload on file change. No
hidden mutation — all writes go through a `ConfigStore` that fires change
events.

## Plugin host (Scripting)

Plugins are .NET assemblies in either:

- `<host install dir>\app-modules\<id>\` — first-party, ships with the MSI.
- `%AppData%\OpenReader\plugins\<id>\` — user-installed, opt-in.

Each plugin folder contains a `manifest.json` (validated by
`PluginManifestFile.TryLoad`) and one DLL. The manifest names the assembly,
the `IAppModule` type, and the plugin API version it was built against. The
host (`PluginHost`):

1. Reads the manifest. Refuses if `apiVersion.Major != PluginApi.CurrentApiVersion.Major`
   or its minor exceeds the host's. (See `PluginApi.IsCompatible`.)
2. Loads the DLL into a fresh `PluginLoadContext` (collectible
   `AssemblyLoadContext`). The contract assemblies — `OpenReader.Abstractions`,
   `OpenReader.Diagnostics`, `Serilog` — are deferred to the host's default
   ALC so that plugin-side `IAppModule` is the *same type* the host references.
3. Activates the manifest-named type via `Activator.CreateInstance` (parameterless
   constructor required).
4. On every focus change, calls `IAppModule.Matches(ProcessInfo)` for each
   loaded plugin. Matching plugins are attached (`OnAttachAsync`); previously
   matching plugins that no longer match are detached (`OnDetachAsync`).
5. Plugin-contributed `SpeechRule`s (registered via
   `IAppContext.RegisterSpeechRule`) live on the per-attach `PluginContext`.
   When the rule set changes, `PluginHost.RulesChanged` fires; the host's
   `Program.cs` rebuilds the rule engine and swaps it atomically via
   `SpeechPipeline.UpdateRuleEngine`.

Hot-reload (dev only, `OPENREADER_DEV=1`): a `FileSystemWatcher` per root
debounces (500 ms) and runs `PluginHost.ReloadAsync`, which:
- Drops any plugin whose manifest file disappeared.
- Reloads any plugin whose dir contents are newer than its `LoadedAtUtc`.
- Loads anything new.
- Re-evaluates matches against the current focus.

Note: on Windows, an mmap'd plugin assembly cannot be deleted from disk
while its ALC has it loaded. `ReloadAsync` unloads the ALC first, but the
file may stay locked until GC actually collects the unloadable context;
in practice this is fine for reloads of the *same* plugin (we replace it
in-place), and uninstalling a plugin while the host runs is supported via
manifest deletion (the DLL stays on disk until next launch).

Lua/JS scripting is a future addition layered on top via embedded interpreter
(consider `MoonSharp` or `Jint`). Out of scope for v0.1.

## Native interop strategy

- **UIA, MSAA, IA2:** `CsWin32` source-generated P/Invoke + `ComWrappers`. No
  hand-rolled RCW.
- **SAPI5:** COM, generated bindings.
- **eSpeak-NG:** P/Invoke into `libespeak-ng.dll`. Bundled binary, MIT-safe
  config (no built-in voices we'd ship under GPL — load voices from disk).
- **liblouis** (when braille lands): P/Invoke into `liblouis.dll`. LGPL —
  dynamic link only.
- **The C++ shim:** `native/win-injector/` builds a tiny DLL that:
  - Gets injected into target processes via `SetWindowsHookEx` or
    `CreateRemoteThread` when (and only when) the display-model code path
    needs it.
  - Hooks GDI/DirectWrite text APIs via Microsoft Detours (MIT, post-2016).
  - Streams captured text to the host over a named pipe.
  - Has zero accessibility logic — it is a dumb sensor.

If we never need the display model (UIA-only universe), this DLL never ships.
That is the goal for v0.1.

## Test strategy

- **Unit tests:** every project, xUnit. Synthetic `IAccessibilityProvider` for
  core/speech/input tests.
- **Integration tests:** drive a real UIA tree via headless WPF/WinForms test
  apps in `tests/integration/`. Verify focus events, speech output text, key
  bindings.
- **Performance tests:** `BenchmarkDotNet` on hot paths (speech rule
  evaluation, tree walks, event dispatch).
- **Smoke tests:** scripted user sessions ("open Notepad, type, navigate") in
  CI on a Windows runner with a virtual display.
