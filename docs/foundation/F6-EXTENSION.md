# F6 — Extension

**Status:** specified, not built.
**Depends on:** F1 (`Filter<Presentation>` needs a presentation to filter).
**Blocks:** the plugin ecosystem, audio themes, relay, third-party engines.

---

## Why

NVDA has a `monkeyPatches/` directory **in core**. That is not carelessness; it
is what happens when the sanctioned extension surface is narrower than what
add-ons need. Add-ons reach into internals, core changes break them invisibly,
and the annual API-breaking `.1` release becomes the loudest recurring complaint
in the ecosystem.

AURA's contract is versioned and gated by `PluginApi.IsCompatible`, which is
better. But **a narrow versioned contract plus .NET reflection produces the same
outcome with less visibility.** A plugin that cannot do what it needs through the
contract will do it through `BindingFlags.NonPublic`, and then a core refactor
breaks it with no compile error anywhere.

Today a plugin can register a `SpeechRule` and react to focus changes. That is
narrower than NVDA in every direction.

`DESIGN_PRINCIPLES.md` already says "we won't let app shims monkey-patch core"
and names the NVDA lesson. Keeping that promise is not a matter of forbidding
it; it is a matter of making it pointless.

---

## The contract

### Extension points

NVDA's `extensionPoints` is the right shape and should be taken directly.
`src/Reader.Abstractions/Extensibility/`

```csharp
/// Notify everyone. Handlers cannot change anything.
public sealed class ActionPoint<T>
{
    public IDisposable Register(Action<T> handler);
    public void Notify(T value);
}

/// Chain: each handler may return a modified value. Order is registration order.
public sealed class FilterPoint<T>
{
    public IDisposable Register(Func<T, T> handler);
    public T Apply(T value);
}

/// Vote: any single "no" wins. Used for suppression.
public sealed class DeciderPoint<T>
{
    public IDisposable Register(Func<T, bool> handler);
    public bool Decide(T value);      // true only if every handler agrees
}
```

Three properties they must have, all learned from NVDA's version:

1. **A handler that throws is logged and skipped, never fatal.** One bad add-on
   must not silence the reader.
2. **Registration returns `IDisposable`** and `PluginContext` disposes them on
   detach — the same lifecycle it already applies to speech rules.
3. **They are strongly typed.** NVDA's are `**kwargs`, and the resulting
   signature drift is a large share of its add-on breakage.

### The points that exist from day one

| Point | Type | What it lets a plugin do |
|---|---|---|
| `Output.FilterPresentation` | `FilterPoint<Presentation>` | Rewrite any announcement — the general form of a speech rule |
| `Output.ShouldSpeak` | `DeciderPoint<Presentation>` | Suppress an announcement |
| `Input.FilterBinding` | `FilterPoint<GestureBinding>` | Claim or remap a key, in context |
| `Focus.Changed` | `ActionPoint<AccessibleNode?>` | Observe |
| `Mode.Changed` | `ActionPoint<ReaderMode>` | Observe |
| `Config.ProfileChanged` | `ActionPoint<string>` | Reconfigure |
| `Interceptor.Attached` | `ActionPoint<ITreeInterceptor>` | React to a document |

`FilterPresentation` is the important one. It subsumes most of what NVDA
add-ons monkeypatch `speech.py` for, and — because a `Presentation` is
segments with kinds — a plugin can drop all `Hint` segments in one app, or
rewrite `Role` segments to shorter words, without string surgery.

### The contract widening 4d already names

Promote to `Reader.Abstractions`, all of them already existing internally:

- `IPluginCommand` — `Id`, `DefaultChord`, `Handler`; host wires it into the
  gesture map **and the rebinding UI**, which is the part that is easy to forget
  and the part users notice.
- `IAudioTheme` (4b), `ISettingsPanel`, `ISpeechEngine`, `IInputSource`.
- New from F3: `ITreeInterceptorProvider`.
- New from F2: `ITextSurfaceProvider` — so a plugin can supply a text model for a
  control the platform reads badly. This is the single most valuable one for app
  modules and it is currently host-internal.

Lifecycle hooks: `OnStartup`, `OnShutdown`, `OnProfileChanged`,
`OnEnabledChanged`.

### The deprecation policy

Written down, because NVDA's absence of one is its ecosystem's largest recurring
cost:

> A public member marked `[Obsolete]` in version *N* keeps working through
> *N+2*. Removal happens only at a major version. Every shim lives in
> `Reader.Abstractions/Compat/` so its cost is visible in one place, and a shim
> that is still there after three releases is a signal the replacement is wrong.

Plus: `PluginApi.CurrentApiVersion` goes to 1.1 with this work, and every v1.0
plugin keeps loading.

### Trust, restated

`DESIGN_PRINCIPLES.md` already states this honestly and it should not drift:
plugins are **trusted code at full trust in-process**. `PluginLoadContext` gives
type-identity isolation, not a security boundary. `capabilities` is advisory.

What this spec adds is a plan for the case where that is not enough — because
"install only what you trust" is fine for an add-on you compiled and wrong for a
one-click store install. Out-of-process hosting for untrusted plugins is a real
option, and it is cheap *for the plugins that only filter presentations*: a
`Presentation` is a serialisable record, so a filter can run over a pipe with a
deadline. Plugins that need an `ITextSurface` or an interceptor cannot. That
split — **out-of-process for observers and filters, in-process for anything on
the text path** — is the honest design, and it should be recorded now even
though it is not built now.

---

## How it will be implemented

`Reader.Abstractions/Extensibility/` — the three point types, ~150 lines total,
no dependencies.

`Reader.Core/Extensibility/ExtensionPoints.cs` — the well-known instances, held
by the host and handed to `PluginContext`. **Not static.** `DESIGN_PRINCIPLES.md`
forbids module-level mutable singletons and that rule is what makes the
synthetic harness possible; extension points are exactly the kind of thing that
"obviously" wants to be static and must not be.

`IAppContext` gains the points, and `PluginContext` tracks registrations for
disposal on detach — the existing `RegisterSpeechRule` tracking is the model.

`SpeechPipeline` calls `FilterPresentation.Apply` then `ShouldSpeak.Decide`
between the rule engine and the arbiter. A dropped presentation is logged with
the deciding plugin's id, so "why did it go quiet" has an answer.

---

## Migration

1. The three point types. Pure addition.
2. Wire `FilterPresentation` and `ShouldSpeak` into `SpeechPipeline`. No
   registered handlers, so no behaviour change.
3. Move the first-party app modules onto `FilterPresentation` where they
   currently use a `SpeechRule` — dogfooding, as Phase 3 did.
4. Promote the named interfaces to `Reader.Abstractions`; bump to API 1.1.
5. `IPluginCommand` + gesture map + rebinding UI.
6. `Reader.Abstractions/Compat/` and the policy in `EXTENSIONS.md`.

---

## Proof it landed

- A plugin suppresses one app's status-bar chatter with a `DeciderPoint`
  registration and no speech rule.
- A plugin that throws in a filter is logged, skipped, and the announcement is
  still spoken.
- A registered handler is gone after `OnDetachAsync` without the plugin doing
  anything.
- A v1.0 plugin still loads against a 1.1 host.
- `samples/SamplePlugin` uses at least one extension point, so the sample stays
  the proof the contract is usable.

---

## Open questions the implementing session must close

1. **Is `FilterPoint<Presentation>` on the hot path a latency problem?** Every
   announcement passes through every registered filter. With ten plugins that is
   ten delegate calls plus whatever they do. Budget it (F5c) and consider
   declaring interest by `SpeechReason` so most filters are not invoked.
2. **Do speech rules and `FilterPresentation` overlap so much that one should
   go?** Rules are data and user-editable; filters are code. Probably both, with
   rules the recommended path. Say so explicitly, or plugin authors will pick
   arbitrarily.
3. **Ordering between filters from different plugins.** Registration order is
   load order, which is directory order, which is arbitrary. NVDA has the same
   problem and it bites. Consider a declared priority in the manifest.
4. **Does the out-of-process split survive contact with `IPluginCommand`?** A
   command handler that wants to speak is fine over a pipe; one that wants to
   inspect the focused element is not.
