# F3 — Navigation: three cursors and one interceptor

**Status:** specified, not built.
**Depends on:** F2 (an interceptor's surface reports structure), F4b (a tree
walk multiplies live COM objects).
**Blocks:** Read mode (4c), terminal support, document support, "what is this?"
inspection.

---

## Why

Two separate gaps, filed together because they are the same seam.

### Gap one: there is no object navigation

NVDA has a **navigator object** — a cursor that walks the raw accessibility tree
independently of focus, with parent / first child / next / previous, plus "move
focus to it" and "click it". It is bound to the numpad on desktop layout and it
is not a niche feature. It is how a blind user:

- reads a status bar that never takes focus
- inspects a toolbar to find out what is on it
- reads a label that is adjacent to a field rather than attached to it
- investigates a control that announces nothing, to find out *why*
- reaches anything in an app that has poor focus handling — which is most old
  apps, and the reason a screen reader needs an escape hatch at all

AURA has a review cursor over **text** and nothing over the **tree**. That is a
missing *axis*, not a missing feature, and it is among the first things a
switching NVDA user will reach for. `IAccessibilityProvider` cannot even express
it: there is `Focused`, `Root`, `FromPoint` and a `ChildrenFactory` on the node,
with no sibling walk and no tree-view filter.

### Gap two: Read mode is a special case of something general

`IReadModeBuffer` is shaped like a web page. But *"an object that takes over
navigation and event handling for a subtree"* also describes:

| Thing | Why it is an interceptor |
|---|---|
| A terminal / console | Output arrives with no focus change; lines scroll; review must follow new output |
| A Word document | Its own text model, its own table navigation, its own structure |
| A spreadsheet | Cells are the navigation unit, not text |
| A chat log | New messages arrive below; the user reads history |
| A PDF | Pages, and a text layer that is not the control tree |

NVDA calls this a tree interceptor and browse mode is one instance of it.
Generalising now costs almost nothing. Generalising after Read mode ships means
retrofitting terminals into a contract that assumed HTML — and the console is
already a known AURA gap (numpad review is dead in PowerShell).

---

## The contract

### The tree walk

`src/Reader.Abstractions/Accessibility/IAccessibilityProvider.cs`

```csharp
public interface IAccessibilityProvider : IAsyncDisposable
{
    // ...existing members...

    AccessibleNode? Parent(NodeId id, TreeView view = TreeView.Control);
    AccessibleNode? FirstChild(NodeId id, TreeView view = TreeView.Control);
    AccessibleNode? LastChild(NodeId id, TreeView view = TreeView.Control);
    AccessibleNode? NextSibling(NodeId id, TreeView view = TreeView.Control);
    AccessibleNode? PreviousSibling(NodeId id, TreeView view = TreeView.Control);

    /// The window that owns this node. Closes the ASSESSMENT S9 layering leak —
    /// the host currently reaches around the interface for this.
    AccessibleNode? ContainingWindow(NodeId id);
}

/// UIA's three tree views, which AT-SPI can also satisfy.
public enum TreeView
{
    /// Everything the provider exposes. Diagnostic use; very noisy.
    Raw,
    /// Interactive and structural elements. The default for object navigation.
    Control,
    /// Only elements carrying information. The right view for a reading pass.
    Content,
}
```

`ChildrenFactory` on `AccessibleNode` stays, but it is now the convenience form
of `FirstChild` + `NextSibling`, not a second mechanism. **Invariant: one tree
walk, filtered by `TreeView`.** A second walk that ignores the view filter is
how "the reader announced a container the user cannot see" happens.

### The cursor triad

`src/Reader.Core/Navigation/Cursors.cs`

```csharp
public sealed class ObjectCursor
{
    public AccessibleNode? Current { get; private set; }
    public TreeView View { get; set; } = TreeView.Control;

    public bool MoveToParent();
    public bool MoveToFirstChild();
    public bool MoveToNext();
    public bool MoveToPrevious();

    /// Follow the system focus. Loose, like the review cursor: called on focus
    /// change unless the user has moved the cursor deliberately since.
    public void FollowFocus(AccessibleNode? focused);

    /// Hand back to the application.
    public bool SetFocusHere();
    public bool ActivateHere();
}
```

The three cursors, written down because "which cursor am I moving" is the
second-largest source of user confusion after mode, and because the *following*
rules are the part everyone gets wrong:

| Cursor | Owns | Moved by | Follows | Reset by |
|---|---|---|---|---|
| **System** | focus + caret, owned by the app | the app; Write-mode keys | — | — |
| **Review** | an `ITextRange` | Reader+arrows | the system caret, loosely | focus change |
| **Object** | a `NodeId` + `TreeView` | Reader+numpad | the system focus, loosely | focus change |

*"Loosely"* means: the cursor is re-synced on a focus or caret change **unless
the user has moved it themselves since the last such event**. Snapping a user's
inspection cursor back the instant an app fires a stray focus event is the
single most complained-about behaviour in every screen reader that gets it
wrong, and it is the same rule `ModeManager` already implements for the manual
mode override — reuse the shape.

### Tree interceptors

`src/Reader.Abstractions/Navigation/ITreeInterceptor.cs`

```csharp
/// <summary>
/// An object that takes over navigation and event handling for a subtree.
/// Read mode is one instance; terminals, documents and spreadsheets are others.
/// </summary>
public interface ITreeInterceptor : IDisposable
{
    /// The subtree this claims. An event on a descendant belongs to it.
    NodeId Root { get; }

    /// The reading surface. For Read mode this is the flattened buffer;
    /// for a terminal, the scrollback.
    ITextSurface Surface { get; }

    /// False when the underlying document has changed out from under it.
    /// Prefer rebuilding over trusting a stale interceptor.
    bool IsCurrent { get; }

    /// The mode this subtree wants on entry. Null means "no opinion".
    ReaderMode? PreferredMode { get; }

    /// Gestures this claims while active — quick-nav letters, table navigation.
    /// Sits above the app layer in the gesture chain.
    GestureLayer Gestures { get; }

    /// Handle an event that landed inside the subtree. Returning true means
    /// the normal announcement path is skipped.
    bool TryHandle(AccessibilityEvent e);
}

/// <summary>Builds an interceptor for a node it recognises. A plugin contract.</summary>
public interface ITreeInterceptorProvider
{
    int Priority { get; }                                   // highest wins
    ITreeInterceptor? TryCreate(AccessibleNode node, IAppContext ctx);
}
```

`IReadModeBuffer` becomes `ITreeInterceptor` **plus** the quick-navigation
members it already has (`FindNext`, `NodeAt`, `Activate`, `SetFocus`). Nothing
in its current definition is lost; it gains a base.

### The gesture chain, completed

`GestureContext` today carries mode, app name and a "has read-mode buffer"
flag. The resolution chain needs the interceptor layer, in NVDA's order — most
specific first:

```
interceptor  →  focused object + ancestors  →  app module  →  user  →  default
```

The missing pieces are the interceptor layer and the per-object layer. The
per-object layer is what lets an app module bind a key on one control without
binding it everywhere in the app, and it is the mechanism NVDA app modules use
most.

---

## How it will be implemented

**`NativeUiaProvider`** gains the walk. `IUIAutomation.ControlViewWalker`,
`ContentViewWalker` and `RawViewWalker` already exist; the work is mapping
`TreeView` onto them, running the walk under the shared cache request so a
navigation step is one round trip, and — critically — **releasing the elements
the walk produces**, which is F4b's problem and the reason F4b comes first.

**`ObjectCursor`** lives in `Reader.Core` and is testable against
`SyntheticAccessibilityProvider` with no Windows. `SyntheticTreeBuilder` already
produces a parented tree; it needs sibling ordering exposed.

**Commands and bindings** — follow NVDA exactly, because every switching user
has these in their fingers:

| Command | Desktop | Laptop |
|---|---|---|
| Navigate to parent | Reader+Numpad8 | Reader+Shift+Up |
| Navigate to first child | Reader+Numpad2 | Reader+Shift+Down |
| Navigate to previous | Reader+Numpad4 | Reader+Shift+Left |
| Navigate to next | Reader+Numpad6 | Reader+Shift+Right |
| Report current object | Reader+Numpad5 | Reader+Shift+O |
| Move focus to object | Reader+Numpad_Minus | Reader+Shift+Backspace |
| Activate object | Reader+Numpad_Enter | Reader+Shift+Enter |
| Move object cursor to focus | Reader+Shift+Numpad_Minus | Reader+Shift+Backspace ×2 |

`KeymapDocumentationTests` will fail until `docs/KEYMAP.md` documents these,
which is the intended behaviour of that test and a good check that the table
above is real.

**Interceptor registry** in `Reader.Core/Navigation/InterceptorManager.cs`:
on focus change, walk ancestors looking for a registered provider that claims
one; attach; detach the previous. Same lifecycle shape as `PluginHost`'s
app-module matching, which already works — copy it rather than inventing.

**The first interceptor to build is the terminal, not Read mode.** It is far
smaller, it fixes a known broken behaviour (PowerShell numpad review), and it
proves the seam against something that is not a web page — which is the whole
point of generalising.

---

## Migration

1. **Add `TreeView` and the walk members** to `IAccessibilityProvider`;
   implement in `SyntheticAccessibilityProvider` first, then `NativeUiaProvider`.
2. **Add `ContainingWindow`** and delete the host's reach-around
   (`Program.cs` `TryGetElement` / `GetTopLevelWindowInfo`). Closes ASSESSMENT S9.
3. **`ObjectCursor` + commands + keymap.** Unit-tested against the synthetic
   tree; verified by ear on the VM. **Ships as a user-visible feature on its own**
   — this is the step that is worth doing early for its own sake.
4. **`ITreeInterceptor` + `InterceptorManager`.** Re-parent `IReadModeBuffer`
   onto it. No behaviour change; nothing implements either yet.
5. **The interceptor gesture layer** in `GestureContext`/`GestureRouter`.
6. **Terminal interceptor.** Fixes console review.
7. **Read mode** (4c) becomes another interceptor.

---

## Proof it landed

- Object navigation walks a synthetic tree — parent, child, siblings — in a unit
  test with no Windows.
- `TreeView.Content` skips a pure-layout container that `TreeView.Control` shows.
- The object cursor follows focus, and stops following once the user has moved
  it, and resumes on the next focus change. *(Same rule as the manual mode
  override; assert it the same way.)*
- "Move focus to object" moves real focus in Notepad on the VM.
- A `SyntheticTreeInterceptor` claims a subtree, claims a gesture, and suppresses
  the normal announcement — proving the seam without a browser.
- Numpad review works in PowerShell. *(Known-broken today.)*

---

## Open questions the implementing session must close

1. **How expensive is a tree walk step over a real Chromium page?** One
   `NextSibling` should be one cached round trip. If it is not, object navigation
   in a browser will be unusable and the cache request needs a second shape.
   Measure alongside R2.
2. **Does the object cursor need its own "review-follows-object" mode?** NVDA
   ties the review cursor to the navigator object — reviewing text reviews the
   *navigator's* text, not the focus's. AURA's review cursor follows the caret.
   These are different models and mixing them badly is worse than either.
   **This is the biggest open design question in this spec.**
3. **What happens to the object cursor when its node dies?** UIA elements go
   stale constantly. Probably: fall back to the nearest live ancestor and say so.
4. **Should `ITreeInterceptor.TryHandle` be able to *rewrite* rather than only
   suppress?** F6's `Filter<Presentation>` may be the better mechanism, in which
   case `TryHandle` stays boolean.
5. **Terminal specifics** — does Windows Terminal's UIA `TextPattern` support
   what a scrollback surface needs, or does it need the console API? NVDA has
   `winConsoleUIA.py` and its comments will answer this faster than experiment.
