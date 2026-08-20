# The thread map

**F4a.** Which threads exist, what each one owns, and what is not allowed to
happen on it.

This document exists because F4b — COM object lifetime — cannot be stated
without it. "Release on the thread that owns the reference" is not a rule you
can follow if nobody has written down which thread that is.

Last updated: 2026-08-20.

---

## The threads

| Thread | Created by | Owns | Must never |
|---|---|---|---|
| **Main** | the CLR, `[STAThread]` | Process lifetime, the single-instance mutex, startup and shutdown ordering | Block on speech or on a UIA call |
| **UI** | `UiThread` | Every WPF window: settings, dialogs, the tray icon and the Aura menu | Touch the accessibility tree, or be waited on by the dispatch loop |
| **UIA callback** | UI Automation, inside the provider process's RPC | Nothing of ours. It arrives, hands us an element, and leaves | Do real work. UIA gives callbacks a budget and starts dropping events if you spend it |
| **Dispatch loop** | `Task.Run` in `NativeUiaProvider.StartAsync` | Mapping elements to `AccessibleNode`, the focus cache, **draining the COM release queue** | Block indefinitely — every cross-process call it makes is timeout-bounded |
| **Keyboard hook** | `SetWindowsHookEx`, low-level | Deciding whether a keystroke is ours, and passing the rest through | Read across a process boundary. Windows silently unregisters a hook that takes too long |
| **Speech** | the engine (SAPI 5 / eSpeak NG) | Synthesis and its own cancellation | Be assumed to be any particular thread — `MarkerReached` arrives on the engine's |
| **Thread pool** | the runtime | Everything `async` that is none of the above | Assume it is the same thread twice |
| **Finalizer** | the CLR | **Nothing. This is the point of F4b.** | Hold, release, or ever see a UIA reference |

---

## The rule that makes the rest work

> **A UIA element never leaves the dispatch loop, and never reaches the
> finalizer.**

Elements are wrapped in `ComOwned<T>` the moment they arrive and handed to a
`ComReleaseQueue`, which the dispatch loop drains between events. `Reader.Core`
never sees one: it sees `AccessibleNode`, which is a neutral snapshot, and that
is what makes the rule containable rather than aspirational.

The one exception is deliberate and explicit: a text surface built on the
focused element outlives the event that produced it, because it is cached while
the caret stays in one control. It takes an **independent reference** through
`ComReleaseQueue.OwnNewReference` and releases it when the surface cache is
invalidated. A borrowed reference would be released under it the moment focus
moved.

### Why not just let the garbage collector do it

Because the garbage collector releases on the finalizer thread, and releasing a
COM proxy is an RPC into the provider process. If that process is wedged, the
releasing thread is wedged. NVDA hit exactly this — issue #11398, "COM objects
are trying to be released at random points in random threads" — and fixed it by
disabling automatic collection entirely and collecting at one known point in
the main loop. AURA reaches the same answer without the bug hunt: disposal
enqueues, and the dispatch loop drains.

The symptom, if this is wrong, is the reader going silent for no reason any log
explains, rarely, and never twice the same way.

---

## The open question: which apartment owns the UIA client

**This is currently an accident and it should not be.**

`Main` is `[STAThread]`. `StartAsync` is reached after several `await`s, and the
main thread has no synchronization context installed — the WPF dispatcher lives
on `UiThread`, not here — so those continuations go to the thread pool, which is
MTA. The thread that creates the UIA client is therefore whichever one the
scheduler happened to supply.

That matters more than it looks. The apartment a COM object is created in
determines how every element it hands out is marshalled for the life of the
process, and whether a release from another thread is a direct call or a
cross-apartment RPC that can block.

**What has been done about it:** the provider logs the thread id, apartment
state, and whether it is a pool thread, at the moment it creates the client. So
the next session on real hardware can read the answer out of the log instead of
reasoning about it.

**What has not:** the client is not yet created on a thread of AURA's own
choosing. The likely fix is a dedicated MTA thread that owns the UIA client and
the dispatch loop together, so "the thread that created it" and "the thread that
releases it" are the same thread by construction rather than by argument. That
is a real behavioural change to the hottest path in the program, and it should
be made with the log in hand rather than blind from Linux.

---

## What to check when the reader freezes

In this order, because it is cheapest first:

1. **`Ctrl+Reader+D`, look at "UIA references".** `live` should be small and
   steady; `released` should climb. Live climbing in step with released, or live
   climbing while released does not, is this class of bug.
2. **The log line at startup** naming the apartment. If it says `STA`, the
   dispatch loop is marshalling cross-apartment on every call.
3. **Whether the freeze survives the application that caused it.** A release
   blocked on a wedged provider unblocks when that provider does.

---

## Related

- [`foundation/F4-LIVENESS.md`](foundation/F4-LIVENESS.md) — the spec this
  implements, including F4c (timeouts, landed) and F4d (backpressure, not
  started).
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — the layering that keeps elements out of
  core in the first place.
