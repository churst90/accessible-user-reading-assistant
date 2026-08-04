# F4 — Liveness: threads, COM lifetime, timeouts, backpressure

**Status:** F4c is two lines and should be done today. F4a/b/d specified.
**Depends on:** nothing.
**Blocks:** F2 and F3 (both multiply live COM objects by a large factor), and
quietly, correctness everywhere.

---

## Why

The worst failure a screen reader has is going silent with no explanation. For a
sighted user a frozen app is an annoyance; for a blind user a frozen screen
reader means the machine is gone — no error, no signal, and no way to diagnose
it, because the diagnostic channel is the thing that stopped.

`ASSESSMENT.md` S1 established this and the Win32 half was fixed:
`Win32Text.cs` uses `SendMessageTimeout` with `SMTO_ABORTIFHUNG` and no bare
`SendMessage` remains. The UIA half was not.

Four sub-problems, in order of how quickly they can be closed.

---

## F4c — Bound every cross-process call *(do this first; it is two lines)*

`NativeUia.Create()` sets `IUIAutomation6.CoalesceEvents` and
`ConnectionRecoveryBehavior`. It does not set the timeouts, which live on
`IUIAutomation2` and are the reason a hung provider can currently block the
dispatch loop forever:

```csharp
internal static IUIAutomation Create()
{
    var automation = (IUIAutomation)Activator.CreateInstance(/* CUIAutomation8 */);

    if (automation is IUIAutomation2 two)
    {
        // A provider that does not answer in this long is not going to.
        // Without these the only dispatch loop blocks with no recovery —
        // the failure ASSESSMENT S1 is about, on the path it did not cover.
        two.ConnectionTimeout  = 1000;   // ms — reaching the provider at all
        two.TransactionTimeout = 2000;   // ms — one call completing
    }

    TryEnableCoalescing(automation);
    return automation;
}
```

Both properties are on `IUIAutomation2`, available since Windows 8, so the cast
will succeed on every supported system. Wrap it anyway — the codebase's existing
`TryEnableCoalescing` pattern is the right shape.

**The numbers are guesses and should be measured.** 2000 ms is longer than any
healthy call by two orders of magnitude and shorter than a user will wait. Once
`PerfTimer` is on the path (F5c), set them from data.

### The half that is still missing

NVDA does more than time out: `watchdog.cancellableExecute` actually **cancels**
a hung outbound RPC rather than waiting for the timeout, using COM call
cancellation. AURA has no equivalent, and a 2-second timeout on every event of a
storm is still a frozen reader.

The escalation ladder should be explicit and implemented in
`ResponsivenessWatchdog`:

| Stage | Trigger | Action |
|---|---|---|
| 1 | Input arrived, no output within 300 ms | Beep. *(Exists today.)* |
| 2 | Still nothing at 1 s | Abandon the in-flight provider call; continue the loop |
| 3 | Three abandonments within 10 s | Tear down and rebuild the UIA client |
| 4 | Rebuild done | **Say so, in speech.** "Reader recovered." |

Stage 4 matters as much as the rest. A user who hears silence and then normal
behaviour learns nothing; a user who hears "reader recovered" knows what
happened and can report it.

---

## F4a — Write down the thread map

There are five threads and no document saying so, which means every new piece of
code makes an assumption that cannot be checked.

| Thread | Owns | Apartment | May touch |
|---|---|---|---|
| **UIA event** (UIA-owned, possibly several) | nothing | MTA | Enqueue a raw event and return. Nothing else. |
| **Dispatch** | focus state, caret state, the object cursor, interceptors | MTA | Provider reads, node mapping, rule evaluation, submitting to the arbiter |
| **Speech drain** | the engine | MTA | `SpeakAsync`, `CancelAsync`. Never provider reads. |
| **UI** | tray, dialogs, settings | **STA** | WPF only. Never provider reads. |
| **Keyboard hook** | nothing | — | Enqueue and return within the hook timeout. Never anything slow. |

Three rules that follow, each enforceable:

1. **A UIA event handler enqueues and returns.** Already true (`RawUiaEvent` →
   channel) and it must stay true; a `Debug.Assert` on the dispatch thread at the
   top of every mapping function makes a regression loud.
2. **Provider reads happen on the dispatch thread only.** Not the speech thread,
   not the UI thread, not the hook.
3. **The keyboard hook never blocks.** A hook that exceeds the OS timeout is
   silently removed by Windows and the reader stops responding to keys with no
   error at all.

Deliverable: `Thread.cs` in `Reader.Diagnostics` with
`ThreadRole.Current` and `AssertOn(ThreadRole)`, plus a section in
`ARCHITECTURE.md`. Cheap, and it turns folklore into a checked fact.

---

## F4b — COM object lifetime is owned, never collected

**This is the inherited mistake, and it is the reason this spec exists.**

NVDA issue #11398 documents freezes whose root cause was: *"COM objects are
trying to be released at random points in random threads due to Python's garbage
collector."* A background thread holding a lock triggers a collection; the
collection calls `Release()` on a COM proxy; that `Release()` blocks on an RPC
into a process that is itself blocked on NVDA. Deadlock. The fix was to disable
automatic GC entirely and call `gc.collect()` at a known point in the main loop.

.NET has the identical hazard with a different name. An `IUIAutomationElement`
RCW that becomes unreachable is released by the **finalizer thread** — a thread
AURA does not control, at a time it does not choose, into a provider that may be
wedged. It will be rare, it will be unreproducible, and it will present as the
reader freezing for no reason.

`NativeUiaProvider` currently passes `IUIAutomationElement` across a channel to
the dispatch loop. Those are exactly the objects whose ownership is undecided.

### The invariant

> **No UIA interface pointer is reachable from a finalizer.**
> Every native element is held by a wrapper with explicit disposal. Release
> happens on the thread that created it, drained from a release queue.

### The implementation

`src/ReaderPlatform.Windows/Interop/ComOwned.cs`

```csharp
/// <summary>
/// Owns a COM pointer with explicit lifetime. Never finalized — an unreleased
/// instance is a leak, which is strictly better than a release on the wrong
/// thread at the wrong time. See NVDA #11398.
/// </summary>
internal sealed class ComOwned<T> : IDisposable where T : class
{
    private T? _value;
    private readonly ComReleaseQueue _queue;

    public T Value => _value ?? throw new ObjectDisposedException(nameof(ComOwned<T>));

    public void Dispose()
    {
        var v = Interlocked.Exchange(ref _value, null);
        if (v is not null) _queue.Enqueue(v);   // released on the owning thread
    }

    // Deliberately no finalizer.
}
```

`ComReleaseQueue` is drained by the dispatch loop between events — the same
"known point in the loop" NVDA arrived at, reached deliberately rather than
after a bug hunt. A leaked element is a bounded amount of memory. A `Release()`
on the finalizer thread is an unbounded freeze.

Supporting rules:

- **Elements do not cross into `Reader.Core`.** They never have — `AccessibleNode`
  is a neutral snapshot — and that is what makes this containable. The rule is now
  explicit rather than incidental.
- **`ComWrappers` with `CreateObjectFlags.UniqueInstance`** so no RCW is cached
  by the runtime and reachable from elsewhere.
- **A debug counter** of live wrappers, surfaced in `DiagnosticSnapshot`. A
  monotonically climbing count is a leak; a count that drops between events means
  the queue is draining.
- **`ITextRange` implementations hold COM ranges** and have the same problem.
  F1's open question 2 — whether a queued `Presentation` may hold an
  `ITextRange` — is answered here: **it may not.** It holds a bookmark, and the
  range is re-acquired on use.

---

## F4d — Backpressure is a policy, not an accident

`CoalesceEvents` helps at the UIA layer. It does not say what happens when the
dispatch channel fills, and today that is unspecified — which means the answer is
whatever `Channel<T>`'s default is, chosen by nobody.

A busy web page can raise thousands of events per second. The policy has to be
per event kind, because the kinds are not equally important:

| Kind | Policy | Why |
|---|---|---|
| Focus, caret | **Never dropped.** Unbounded, or bounded with the oldest dropped | Losing one is losing the user's place |
| Value, state | Coalesce by element; keep the newest | Only the current value matters |
| Live region, notification | Bounded; drop oldest and **count** | A chat page can produce these without limit |
| Structure changed | Coalesce to a single "something changed" flag per interceptor | The interceptor rebuilds; N events and 1 are the same instruction |

And the rule that makes it debuggable: **every drop is counted, and the counts
are in `DiagnosticSnapshot`.** Silent truncation reads as "the reader ignored
me", and an unexplained miss is indistinguishable from a bug in the rules.

---

## Migration

1. **F4c timeouts.** Two lines. Today.
2. **Thread map + `AssertOn`.** A day. No behaviour change.
3. **`ComOwned` + release queue**, applied to the event path first, then the
   text path. Before F2/F3.
4. **Watchdog escalation ladder**, stages 2–4.
5. **Bounded channels with per-kind policy + drop counters.**

---

## Proof it landed

- A synthetic provider that blocks for 10 s on every read: the reader still
  answers a keystroke, beeps, and recovers.
- The live-wrapper count returns to its baseline after a burst of 10,000 focus
  events. *(Leak check; run it long enough that a finalizer would have run.)*
- A flood of 10,000 live-region events in one second does not delay a focus
  announcement by more than the budget, and the drop count is non-zero and
  visible.
- `AssertOn(ThreadRole.Dispatch)` fires in a debug build if node mapping is
  called from anywhere else.
- No `System.__ComObject` finalizer appears in a memory profile of a 30-minute
  session on the VM.

---

## Open questions the implementing session must close

1. **Does `IUIAutomation2.TransactionTimeout` actually bound a
   `BuildUpdatedCache` over a large subtree,** or only individual property
   fetches? This matters for R2 and for Read mode: a timeout that does not cover
   the bulk fetch leaves the worst call unbounded.
2. **Can a hung UIA call be cancelled at all from .NET,** or only waited out?
   NVDA uses COM call cancellation from C++. If .NET cannot, stage 2 of the
   ladder becomes "abandon the thread and continue on a fresh one", which leaks a
   thread per incident — acceptable, but it should be a decision, not a discovery.
3. **Is a leaked element really better than a finalizer release?** Stated as
   obvious above; confirm the leak is bounded in practice by measuring stage 3.
4. **Does `CoalesceEvents` change event ordering** in a way the arbiter's
   coincidence window depends on? The arbiter assumes producers describing one
   action arrive within ~120 ms of each other.
5. **Where does the release queue drain if the dispatch loop is itself
   blocked?** Circular by construction. Possibly a dedicated release thread with
   its own timeout is the honest answer.
