# F2 — Structured text

**Status:** specified, not built.
**Depends on:** F1 (structure becomes `SegmentKind.Structure` segments).
**Blocks:** Read mode (4c), table navigation, list and heading announcement,
braille structure indicators.

---

## Why

`ITextRange.GetAttributes()` answers *"is this whole range bold?"* It returns a
flat dictionary and it drops any attribute that is not uniform across the range
— which is correct for what it does, and useless for the question Read mode
actually asks:

> Moving from here to there, what did I **enter** and what did I **leave**?

That question is most of what a document reader says. "List, five items. Bullet.
Buy milk. … Out of list." requires knowing a list was entered, at what nesting
depth, and later left. "Table, 3 by 4. Row 1, column 1, Name." requires the same
for tables. Heading level, link, blockquote, code block, frame, landmark — all
of them are *boundaries crossed*, not *attributes of a span*.

NVDA's answer is `TextInfo.getTextWithFields()`, which returns text interleaved
with `FieldCommand("controlStart" | "controlEnd" | "formatChange", field)`. It is
the second half of the `TextInfo` idea. AURA took the first half and stopped.

**The cost of not having it** is specific and predictable: `IReadModeBuffer`
will read text from the buffer and re-derive structure by walking the tree. Two
sources of truth for one document, drifting — which is precisely the failure
`IReadModeBuffer`'s own doc comment says the design exists to prevent. The
buffer would be repeating the mistake it was written to avoid.

---

## The contract

`src/Reader.Abstractions/Text/TextSegment.cs`

```csharp
public enum StructureEdge { Enter, Exit }

public abstract record TextSegment;

/// A run of text whose attributes are uniform.
public sealed record TextRun(
    string Text,
    IReadOnlyDictionary<string, object?> Attributes) : TextSegment;

/// Entering or leaving a control that encloses text.
public sealed record StructureMarker(
    StructureEdge Edge,
    AccessibleRole Role,
    IReadOnlyDictionary<string, object?> Attributes) : TextSegment;
```

Added to `ITextRange` as a default interface method, so no existing backend
breaks and no backend is forced to implement it before it can:

```csharp
public interface ITextRange
{
    // ...existing members unchanged...

    /// <summary>
    /// The content of this range as runs and structure boundaries, in document
    /// order. The default is a single run — correct for any backend with no
    /// structure to report, which is every backend that ships today.
    /// </summary>
    IReadOnlyList<TextSegment> GetContent() =>
        [new TextRun(GetText(-1), GetAttributes())];
}
```

### Well-known structure attributes

Extend `TextAttributes` with the keys a `StructureMarker` carries. Strings, for
the reason already recorded there — a plugin can introduce one without a
contract bump.

```csharp
public const string Level        = "level";        // int — heading depth, list nesting
public const string PositionInSet = "positionInSet"; // int
public const string SizeOfSet    = "sizeOfSet";    // int
public const string RowCount     = "rowCount";     // int — tables
public const string ColumnCount  = "columnCount";  // int
public const string RowIndex     = "rowIndex";     // int — cells, 1-based
public const string ColumnIndex  = "columnIndex";  // int
public const string RowHeader    = "rowHeader";    // string
public const string ColumnHeader = "columnHeader"; // string
public const string Landmark     = "landmark";     // string — "navigation", "main"
public const string IsLayout     = "isLayout";     // bool — see below
public const string Description  = "description";  // string
```

### `IsLayout` — the attribute that matters most and is easiest to forget

A table used for page layout must not be announced as a table. NVDA gates this
in `ControlField.getPresentationCategory` (`textInfos/__init__.py:71`), which
returns `PRESCAT_LAYOUT` for a table marked `table-layout`, and `PRESCAT_LAYOUT`
means "say nothing at all". Web pages are still full of layout tables and
announcing them makes a page unreadable.

The backend sets `IsLayout`; the *decision* about what to do with it belongs in
the rules, not in the backend — see below.

### Presentation category is a rule, not a method

NVDA's `getPresentationCategory` is ~120 lines of `if role == X and not
formatConfig["reportY"]` inside `TextInfo`. It is the single clearest example in
NVDA's tree of a policy that should have been data. It hard-codes: which roles
are single-line, which are containers, which are markers, which are layout, and
a dozen verbosity switches.

**AURA already has the right machine for this.** A `StructureMarker` becomes a
`SpeechRequest` with `Reason = StructureEntered` / `StructureExited`, and
`assets/rules/defaults.yaml` decides what it says — including saying nothing.
That means "don't announce blockquotes", "announce list nesting only past level
2", "announce tables but not layout tables" are user-editable rules with a rule
trace, not code.

This is one of the places where AURA should be *better than* NVDA rather than
equivalent, and it costs nothing extra because the rule engine already exists.

New `SpeechReason` members: `StructureEntered`, `StructureExited`.
New `SegmentKind` usage: these render as `SegmentKind.Structure`.

### Table units

`TextUnit` currently stops at `Document`. Table navigation is unexpressible
without these, and NVDA has had them since forever
(`textInfos/__init__.py:317`):

```csharp
Table,   // the whole table
Row,
Column,
Cell,
```

Plus `ReadingChunk` — NVDA's say-all unit, which resolves to sentence or
paragraph per configuration. Say-all currently walks by line, which is why long
paragraphs read choppily.

---

## How it will be implemented

**`StringTextSurface`** keeps the default implementation. It has no structure.
This is deliberate: the reference backend stays the simplest possible thing.

**`NativeUiaTextRange.GetContent()`** is the real work. UIA gives structure two
ways and both are needed:

1. `ITextRangeProvider::GetChildren()` returns the elements enclosed by the
   range. Walking those, and comparing each child's range against the parent's,
   yields enter/exit boundaries.
2. `ITextRangeProvider::GetEnclosingElement()` gives the innermost container, so
   a range that starts *inside* a list still reports the list as already
   entered — which matters because a reader rarely starts at the document root.

The algorithm, stated so it is not re-derived:

```
GetContent(range):
    ancestors = walk GetEnclosingElement() upward to the document root
    emit Enter for each ancestor, outermost first   # context on arrival
    for each child in range.GetChildren():
        emit TextRun for the text between the cursor and the child's start
        emit Enter(child.role, attributes)
        recurse into the child's range
        emit Exit(child.role)
    emit TextRun for the remaining text
    emit Exit for each ancestor, innermost first
```

Attribute reads are the cost. `NativeUiaTextRange` already reads attributes one
`GetAttributeValue` call at a time, and `TEXT_MODEL.md` already flags this as an
open question with the right answer: **add a `GetAttributes(params string[]
keys)` overload so callers ask only for what they will use.** Do that as part of
this work — Read mode calls `GetContent` per line, and per-attribute round trips
per line is exactly the shape of problem F4/R2 exists to catch.

**`IReadModeBuffer.GetContent()`** is its *native* output. For a virtual buffer,
structured content is what it has; flat `GetText()` is the derived thing. When
4c is built, `GetText()` should be implemented in terms of `GetContent()` and
not the other way round.

**Caret following** gains a use for this immediately, before Read mode: moving
the caret out of a list item and into the next paragraph currently says only the
paragraph. With `GetContent`, `CaretMotionResolver` can report the boundaries
crossed, which is the behaviour NVDA has and AURA visibly lacks in Word-like
editors.

---

## Migration

1. **Add the types and the default interface method.** Pure addition. Nothing
   implements it; nothing regresses.
2. **`StringTextSurface` conformance test for the default** — a plain string
   yields exactly one `TextRun` and no markers.
3. **Add the `GetAttributes(params string[])` overload** and route the existing
   call sites through it. Measurable win on its own.
4. **Implement `NativeUiaTextRange.GetContent()`.** Verify on the VM against a
   real web page and a Word document; those are the two providers that actually
   populate structure.
5. **Add `StructureEntered`/`StructureExited` reasons and default rules.**
   Announcement of lists, headings, tables and links starts working here, in
   ordinary editors, before Read mode exists.
6. **Add the table units** and a `TableNavigation` command set
   (Ctrl+Alt+arrows — NVDA and JAWS both use it; do not invent).
7. **`ReadingChunk`** and say-all pacing.

Steps 1–3 are safe. Step 4 is where measurement matters. Step 5 is the first
user-visible payoff and is worth reaching quickly, because it makes ordinary
document reading better without waiting for 4c.

---

## Proof it landed

- A synthetic nested list renders
  `list, 2 items / bullet, one / list, 1 item / bullet, one point one / out of list / out of list`
  through `TranscriptRenderer`.
- A layout table renders no table announcement; a data table renders "table, 3
  by 4".
- A range starting mid-list reports the enclosing list as entered.
- Moving the caret from the last list item to the following paragraph announces
  "out of list".
- `GetContent()` on `StringTextSurface` returns one run — the default is not
  accidentally overridden.
- Attribute fetch for one line of a real web page costs one round trip, not one
  per attribute. *(Measured, not asserted.)*

---

## Open questions the implementing session must close

1. **Does `GetChildren()` work adequately on Chromium's native UIA provider?**
   The whole algorithm rests on it. This is the first thing to test on the VM and
   it may change the approach. If it is slow or incomplete, the fallback is
   walking the element tree in parallel with the text range, which is worse but
   workable.
2. **Where do "entered" boundaries come from on arrival at a control** that is
   not a document — a plain edit box inside a group box? Probably from
   `AccessibleNode` ancestry rather than from the text range, which means two
   producers for structure. Decide which owns it.
3. **Does `formatChange` need a distinct segment type?** NVDA has three field
   commands; this spec has two (`TextRun` carries attributes, so a format change
   is implicit in a run boundary). Confirm that nothing needs an explicit format
   *change* event.
4. **How much structure should be announced on a plain caret move?** NVDA's
   answer is "boundaries crossed, filtered by verbosity". Getting this wrong in
   either direction is very noticeable. Needs listening on the VM, not reasoning.
5. **Table headers: computed or provided?** UIA's `TablePattern` /
   `TableItemPattern` give header elements, but many providers get them wrong.
   Decide whether to trust them or to infer from the first row/column.
