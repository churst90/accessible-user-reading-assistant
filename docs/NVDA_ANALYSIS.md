# NVDA: a full analysis

Written 2026-08-03, against NVDA 2026.1.1.

The README says Aura is "inspired by NVDA. Not a port. Not a fork." That is a
statement of intent, and until now it has not been backed by an actual reading
of what NVDA is. This document is that reading: what NVDA is made of, which of
its ideas are load-bearing and must be taken, which of its decisions are scar
tissue and must not be, and — the part that matters most for planning — how
large the distance actually is between where Aura stands today and a screen
reader someone would choose over NVDA.

The short version, stated up front so the rest can be checked against it:

- **NVDA's best ideas are three**: `TextInfo` (a text range abstraction), the
  speech *sequence* (speech as a list of text and commands, not a string), and
  the tree interceptor (an object that takes over navigation for a subtree).
  Aura has the first. It has neither of the other two, and both are
  contract-level holes that get more expensive every week they stay open.
- **NVDA's worst problems are two**, and Aura has structurally avoided one and
  structurally inherited the other. It has avoided the single-threaded Python
  core. It has *not* avoided handing COM object lifetime to a garbage
  collector, which is the documented cause of NVDA's worst freezes.
- **The capability gap is much larger than the roadmap admits.** NVDA is
  roughly 45 subsystems. Aura has 8 of them. "First in class" is not reachable
  by finishing Phase 4; it needs a scoreboard and a decade-scale sequencing
  decision about what is deliberately never built.
- **One external fact changed the plan this year.** Chromium enabled native UI
  Automation by default in Chrome 138 and removed the legacy escape hatch in
  Chrome 147 (April 2026). A UIA-only Read mode over Chromium browsers is now
  viable. It was not viable eighteen months ago, and it is still not viable for
  Firefox.

---

## 1. What NVDA is, structurally

NVDA is ~45 top-level subsystems in `source/`, plus a C++ tree in
`nvdaHelper/`. Grouped by what they do, because the directory listing alone
does not tell you where the weight is:

| Group | Subsystems |
|---|---|
| **Core loop** | `core.py`, `queueHandler`, `eventHandler`, `watchdog`, `extensionPoints`, `monkeyPatches`, `_asyncioEventLoop` |
| **Object model** | `NVDAObjects/` (IAccessible, UIA, window, JAB, behaviors), `controlTypes/`, `api.py` |
| **Text model** | `textInfos/`, `textUtils/`, `documentNavigation/` |
| **Document/web** | `virtualBuffers/`, `browseMode.py`, `treeInterceptorHandler.py`, `nvdaHelper/vbufBackends/` |
| **Platform APIs** | `UIAHandler/`, `IAccessibleHandler/`, `comInterfaces/`, `winAPI/`, `winBindings/`, `COMRegistrationFixes/` |
| **Injection** | `NVDAHelper/` + `nvdaHelper/` (C++: `remote`, `local`, `vbufBase`) |
| **Speech out** | `speech/`, `synthDrivers/`, `_synthDrivers32/`, `speechDictHandler/`, `characterProcessing` |
| **Braille out** | `braille/`, `brailleDisplayDrivers/`, `brailleTables/`, `brailleViewer/`, `tactile/` |
| **Audio out** | `audio/`, `waves/`, `nvwave` |
| **Input** | `inputCore.py`, `keyboardHandler`, `brailleInput`, `touchHandler`, `hwIo`, `ftdi2` |
| **Extension** | `addonHandler/`, `addonStore/`, `appModules/`, `globalPlugins/` |
| **Vision** | `vision/`, `visionEnhancementProviders/`, `screenCurtain/`, `_magnifier/` |
| **Content** | `contentRecog/` (OCR), `mathPres/` (MathCAT), `displayModel.py` |
| **Remote** | `_remoteClient/`, `_bridge/` |
| **Config/UI/locale** | `config/`, `gui/`, `locale/`, `fonts/`, `images/` |

2026.1 is built on Python 3.13.12, 64-bit only, having dropped 32-bit and
Windows 8.1, and relicensed to GPL-2-or-later.

The thing to take from this table is not any individual entry. It is the
proportion: **roughly a third of NVDA is content access, a third is output, and
only a small slice is the reader logic in the middle.** Aura today is almost
entirely the middle slice. That is the correct place to have started, and it is
also why the remaining work is larger than it looks from inside the codebase.

---

## 2. The ideas that are load-bearing, and must be taken

These are not "nice patterns". Each one is the reason some later feature was
possible at all, and in every case NVDA's competitors that skipped it paid for
it permanently.

### 2.1 `TextInfo` — a text range is a type

A positioned, movable, comparable span over a text-bearing object, with `move`,
`expand`, `collapse`, `compareEndPoints`, `setEndPoint`, `bookmark`. Caret
following, review, say-all, selection reporting, braille rendering and browse
mode are all written against it, so a new backend gets all six behaviours at
once.

**Aura has this** (`ITextRange` / `ITextSurface` / `ITextSurfaceProvider`,
`docs/TEXT_MODEL.md`) and it was the right call. It is the single largest thing
already banked.

### 2.2 The speech sequence — output is a list, not a string

`speech.speak()` takes a *sequence*: interleaved `str` and command objects.
`LangChangeCommand`, `PitchCommand`, `RateCommand`, `VolumeCommand`,
`BreakCommand`, `CharacterModeCommand`, `PhonemeCommand`, `IndexCommand`,
`CallbackCommand`, `BeepCommand`, `WaveFileCommand`, `EndUtteranceCommand`.

Every one of those exists because a flat string could not express something a
user needed:

| Command | The user-visible thing that needs it |
|---|---|
| `LangChangeCommand` | A French quotation inside an English page, read in a French voice |
| `PitchCommand` | Capital letters raised in pitch instead of announced as "cap" |
| `IndexCommand` + `CallbackCommand` | Say-all that knows *where it has got to* — so it can scroll braille, move the caret, and resume after an interruption at the right word |
| `BeepCommand` / `WaveFileCommand` | An earcon *inside* an utterance, not queued behind it |
| `BreakCommand` | A pause between a control's name and its value that does not depend on punctuation |
| `EndUtteranceCommand` | Chunking a long document so the synth starts speaking before the whole thing is composed |
| `CharacterModeCommand` | Spelling a word without re-synthesising it as a word |

**Aura does not have this.** `SpeechUtterance` is `record(string Text,
ProsodyHint Prosody, string? VoiceId, ...)`. One string, one prosody setting,
one voice, for the whole utterance.

This is the most consequential gap in the codebase, and it is worse than it
looks, because the text model *already knows* about the thing it cannot
express: `TextAttributes.Language` is defined, `NativeUiaTextRange` reads it,
and there is no way to carry it downstream. Every feature in the table above is
either impossible or requires a parallel path around the speech pipeline. Three
of them — say-all resumption, per-language voices, earcons — are already on the
roadmap as 4a/4b/4c work.

### 2.3 Field commands — text carries its structure

`TextInfo.getTextWithFields()` returns text interleaved with
`FieldCommand("controlStart" | "controlEnd" | "formatChange", field)`. That is
how a screen reader can say "list, five items, bullet, buy milk … out of list"
— it knows it *entered* a list and later *left* it, and it knows the nesting.

This is a different thing from an attribute dictionary over a range, and Aura
currently has only the second. `ITextRange.GetAttributes()` answers "is this
whole range bold?" It cannot answer "what did I enter and leave while moving
from here to there?", and structure boundaries are exactly what Read mode
announces.

Without it, Read mode ends up re-deriving structure from the tree in parallel
with reading text from the buffer — two sources, drifting, which is the failure
`IReadModeBuffer`'s own doc comment is written to prevent.

### 2.4 Tree interceptors — Read mode is a special case of something general

`treeInterceptorHandler` is more general than browse mode. A tree interceptor
is *any* object that takes over event handling and navigation for a whole
subtree. Browse mode is one. So are: a terminal (line-based, output arrives
without focus moving), a Word document, a chat log, a spreadsheet, a PDF.

Aura's `IReadModeBuffer` is the browse-mode-shaped half of this. Generalising
it now costs almost nothing; generalising it after Read mode ships means
retrofitting terminals and documents into a contract that assumed a web page.

### 2.5 `extensionPoints` — typed decoupling, so add-ons need not monkeypatch

`Action` (notify), `Filter` (transform a value in a chain), `Decider` (vote;
any veto wins), `Chain`. Core code declares an extension point; add-ons
register against it. `speech.extensions.filter_speechSequence` and
`decide_shouldDoSpeech` mean an add-on can rewrite or suppress speech without
touching `speech.py`.

Aura's plugin surface today is "register a `SpeechRule`" plus a focus-changed
callback. That is narrower than a `Filter`, and narrowness is what *causes*
monkeypatching — NVDA has a `monkeyPatches/` directory in core precisely
because the sanctioned surface was too small at some point.

### 2.6 The script resolution chain — bindings are contextual, and the order is a design

A gesture resolves through, in order: tree interceptor → focused object and its
ancestors → global plugins → app module → braille display → global commands.
Most specific wins; anything in the chain can claim a key.

Aura has the concept (`GestureContext`, layered gesture map, `docs/KEYMAP.md`)
but not the full chain — there is no per-object or per-interceptor layer. That
matters the moment Read mode exists, because Read mode's `h` must be claimable
by an app module for a site that uses `h` for something else.

### 2.7 `characterProcessing` and speech dictionaries — pronunciation is data, per locale

Symbol level (none/some/most/all/character), per-locale symbol dictionaries,
character descriptions ("alpha, bravo" for spelling), and three tiers of user
speech dictionary (default, per-voice, temporary). All data files, all
translatable.

Aura has `PunctuationFilter` and rule-based rewriting, which is a better *engine*
than NVDA's, running on no locale data at all. The engine is the hard part and
it is done; the data is the long part and it has not started.

### 2.8 `watchdog` — freeze detection with actual cancellation

Two halves. The alarm: a thread that notices the core has not ticked and plays
a tone. And the recovery: `watchdog.cancellableExecute`, which runs a COM call
such that a hung outbound RPC can actually be *cancelled* rather than waited on.

Aura has the alarm (`ResponsivenessWatchdog`) and not the recovery. It has
`SendMessageTimeout` for Win32, which covers the Win32 half, and nothing for a
UIA COM call that never returns.

---

## 3. The mistakes, and which ones Aura has already inherited

### 3.1 One Python thread does everything — *avoided*

`core.py` runs a wx main loop; `queueHandler` pumps generators cooperatively;
every event handler, every script, and every synth callback runs on that one
thread. Any blocking call anywhere stalls the entire reader. `watchdog` exists
because this is unfixable without a rewrite.

Aura's dispatch-thread / speech-thread / UI-thread split with channels is
genuinely better and should not be revisited. This is the clearest win in the
project.

### 3.2 COM lifetime delegated to the garbage collector — **inherited**

NVDA issue #11398 is worth reading in full. The diagnosis: *"COM objects are
trying to be released at random points in random threads due to Python's
garbage collector."* A background thread holding a lock triggers a collection,
the collection calls `Release()` on a COM proxy, that `Release()` blocks on an
RPC to a process that is itself blocked on NVDA — deadlock. The mitigation was
to disable automatic GC entirely and call `gc.collect()` at a known point in
the main loop.

**.NET has exactly this hazard.** A UIA RCW released by the finalizer thread
issues its `Release()` from a thread Aura does not control, at a time it does
not choose, potentially into a hung provider. `NativeUiaProvider` holds
`IUIAutomationElement` references across a channel. Nothing in the tree
currently states who owns their lifetime or which thread releases them.

This is not theoretical and it is not a bug to be found later — it is a design
decision that has not been made. It belongs in the foundation, before more COM
code is written on top of the assumption that the runtime will handle it.

### 3.3 In-process injection is load-bearing — *deferred, on unmeasured grounds*

NVDA injects `nvdaHelperRemote.dll` into essentially every GUI process. Browse
mode's virtual buffer is built *inside the target process* by C++ and streamed
out, because building it across a process boundary was measured to be too slow
— the Firefox multi-process transition is documented as having had "a
substantial impact on performance" for exactly this reason, and the fix was
`IAccessibleHypertext2` batching plus keeping the render in-process.

Aura's `ROADMAP.md` puts display-model hooking at 4h, "defer indefinitely",
with the reasoning "UIA covers ~95%". That reasoning is about *coverage* and it
is roughly right. But the injection question is not only about coverage — it is
about whether a virtual buffer over a large document can be built
cross-process within budget. **That has never been measured, and the whole Read
mode design rests on it.** Native UIA with a bulk `CacheRequest` and
`BuildUpdatedCache` over a subtree is a genuinely different proposition from
what NVDA faced with IA2, so the answer may well be yes. It needs to be a
measured yes.

### 3.4 Add-ons are trusted, unversioned, and break annually — *partly avoided*

API-breaking releases happen once a year in `.1` releases. Add-on authors track
`minimumNVDAVersion` / `lastTestedNVDAVersion` and the store gates on them.
Meanwhile `monkeyPatches/` exists in core, and in practice add-ons reach into
internals because the sanctioned surface does not reach far enough.

Aura's versioned contract with a major/minor compatibility gate is better —
*if* the contract is wide enough. A narrow contract plus .NET reflection
produces the same outcome with less visibility. The lesson is not "version the
API", which Aura has done; it is "widen the API until reaching around it is
pointless", which Aura has not.

### 3.5 Speech and braille formatting are duplicated — *available to avoid, not yet avoided*

`speech.speakTextInfo` and `braille.TextInfoRegion` both consume field commands
and both decide what a control's presentation is, with different rules. Users
notice: braille shows something speech did not say. It is a long tail of small
inconsistencies that can never be closed because there is no single definition
to close them against.

Aura has no braille yet, which means it still has the choice. Making it later
is making it wrong: whatever gets built for speech becomes the thing braille
has to be bolted onto.

### 3.6 No behavioural regression net

NVDA has unit tests and a Robot Framework system suite, but the question a
screen reader actually needs answered — *"given this tree and these keystrokes,
what did it say, in what order?"* — is not asserted broadly. Regressions in
announcement behaviour ship, get reported by users, and get fixed one at a
time.

`DESIGN_PRINCIPLES.md` already identifies this ("NVDA tests mostly run against
live applications. That's why subtle regressions ship") and the synthetic tree
harness exists. The harness is half of the answer. The other half — recording
and diffing the transcript — does not exist yet, and it is the single highest-
leverage thing available to this project, because it is a structural advantage
no competitor can retrofit.

### 3.7 Configuration is a stringly-typed global

`config.conf` is a `configobj` singleton with a spec file; profiles layer over
it with triggers that interact in ways that are hard to predict. Aura's typed
POCO graph with explicit layering is better and needs no change.

---

## 4. The capability gap, stated honestly

This is the part that decides whether "first in class" is a plan or a slogan.
Every row is something a real NVDA user relies on today.

**Content access.** IAccessible2 (Firefox, and the battle-tested path for
Chromium), Java Access Bridge, Windows console and terminal, Office COM object
models for Word/Excel/Outlook, Acrobat/PDF, display-model text capture for
legacy GDI apps. *Aura: UIA only.*

**Document structure.** Table navigation with row/column headers, Elements List
(links/headings/landmarks/form fields in a dialog), landmark navigation,
annotations, spelling and grammar reporting, on-demand formatting reporting,
math via MathCAT. *Aura: none. Table navigation in particular is a top-tier
user feature with no roadmap entry at all.*

**Object navigation.** NVDA's navigator object — a second cursor that walks the
raw accessibility tree independent of focus, with parent/child/next/previous
and "move focus to navigator". This is how a blind user finds things that are
not focusable and not in the text. *Aura has a review cursor over text and no
object navigation whatsoever.* This is a missing **axis**, not a missing
feature.

**Output.** Braille: liblouis, contracted tables, ~50 display drivers, braille
input, tethering to focus or review, a braille viewer. Speech: per-language
automatic voice switching, symbol level, character descriptions, three tiers of
speech dictionary, rate boost to 6×. Audio: WASAPI, ducking, sound split, wave
cues. *Aura: SAPI 5 and eSpeak NG, rule-based rewriting, no braille, no locale
data, no mixer.*

**Input.** Touch gestures, braille input, input help mode (press a key, hear
what it does, without doing it). *Aura: keyboard only. Input help mode is cheap
and should not be missing.*

**System integration.** uiAccess for elevated windows, a SYSTEM instance for
the secure desktop and logon screen, portable copies, remote access (built in
since 2025.1, including secure desktop), screen curtain, COM registration
repair. *Aura: manifests written, certificate not obtained — so none of it
works, including the keyboard hook in any elevated window.*

**Ecosystem.** An add-on store with update channels, VirusTotal results and
changelogs; on the order of a thousand add-ons; 60+ locales with translated
documentation. *Aura: a plugin loader, four scaffold app modules, one locale.*

**Diagnostics.** Speech viewer, braille viewer, log viewer, input help, "report
current focus/navigator object". *Aura: a log file, and `RuleTrace` — which is
better than anything NVDA has, and unexposed.*

Counted generously, Aura implements 8 of NVDA's ~45 subsystems. The gap is not
a punch list. It is a sequencing problem, and the honest response to it is a
scoreboard plus an explicit list of things that will deliberately never be
built.

---

## 5. Where being better is actually available

Feature parity is not reachable and is not the goal. These are the places where
NVDA is structurally weak, where the weakness is caused by a decision it cannot
now reverse, and where Aura's existing foundation is already pointed at the
gap.

**Predictable latency.** NVDA's floor is a cooperative single-threaded Python
loop. Aura's is a channel-based multi-threaded C# pipeline with a measured
budget. This is real and it is already banked — but it is unmeasured, and an
unmeasured principle is a wish.

**It never goes silent.** The worst screen reader failure is silence with no
explanation. NVDA's answer is a watchdog bolted onto an architecture that
cannot avoid the freeze. Aura can make "the reader always responds, even when
the app is dead" an actual invariant: bounded calls, cancellable RPC, a
watchdog that escalates to rebuilding the provider rather than beeping at it.
No screen reader currently offers this as a guarantee.

**It can explain itself.** `SpeechUtterance.RuleTrace` already records which
rules produced an announcement. A "why did it just say that?" command, and a
speech viewer that shows the trace, is a first — for users, for support, and
for the add-on authors who currently debug by bisecting.

**Customisation as data.** NVDA's speech behaviour is Python spread across
`speech/`, patched by add-ons. Aura's is YAML rules through one engine, layered
by user/profile/app. This is the thing NVDA users ask for most and cannot have.

**Behavioural regression testing.** Golden transcripts over synthetic trees.
Nobody in this field has it. It compounds: every bug fixed becomes a test that
holds forever, which is what makes a small team able to move at all.

**Read/Write instead of browse/focus.** Small, and worth doing anyway — the
naming failure is twenty years old and users still cannot report which mode
they were in.

**Modern speech done properly.** Neural TTS with streaming and a low-latency
first-word, per-language switching, and a mixer that puts earcons inside the
utterance rather than behind it. NVDA's synth driver model predates all of it.

---

## 6. The one external fact that changed the plan

`docs/READ_WRITE_MODES.md` and `ROADMAP.md` 4c assume Read mode is built over
UIA. Until recently that assumption was in real trouble, and it is worth
recording why it is now survivable — and exactly where it still is not.

**The case against UIA for web content** is made in detail by Jamie Teh (NVDA's
co-founder) in March 2025: UIA lacks semantic control types for many web roles,
lacks most landmark types, maps `aria-errormessage` onto `ControllerFor` with
no indication it is an error, allows only a single `LabeledBy` target, gives
live-region events too coarse to say *what* changed, and treats ARIA as a
second-class bolt-on. NVDA and JAWS both use IAccessible2 for browsers as a
result. That is a serious list from a serious source and it does not go away.

**What changed** is Chromium. From Chrome 138, Chromium-based browsers enable
native UIA by default — a real UIA provider owned by Chromium engineers, not
the old Windows MSAA→UIA proxy shim — and the `UiAutomationProviderEnabled`
policy that let organisations revert was removed in Chrome 147 (7 April 2026).
The proxy layer, which was the main source of the old performance argument, is
gone.

So the position for Aura is:

- **Chromium (Edge, Chrome, Brave): viable over native UIA today.** Ship Read
  mode here first. This is most of the desktop.
- **Firefox: not viable over UIA.** Gecko has no native UIA provider; the
  Windows IA2→UIA proxy is not a substitute. Firefox needs an IAccessible2
  backend, which is a second `IAccessibilityProvider` implementation and a
  significant piece of work. **Say so in the roadmap rather than discovering it
  during 4c.**
- **Teh's semantic gaps still apply**, and they are the reason Read mode's
  quality ceiling over UIA is lower than NVDA's over IA2 for some pages. That
  is an acceptable v1 trade, made knowingly, not a thing to find out later.

This also means `DESIGN_PRINCIPLES.md`'s "we won't reinvent UIA" needs one
sentence of qualification. The principle is right — do not build a parallel
object model. But "UIA is the only backend" is a different claim, it is not
what the principle says, and it is not true for the browser Aura's own first-
party app module already names.

---

## 7. What this document concludes

1. Three contract-level holes must be closed before more features are built on
   top of them: the speech sequence, structured text, and COM lifetime
   ownership. All three get more expensive per week, and all three are cheap
   today.
2. Two whole axes are missing rather than incomplete: object navigation and a
   presentation model shared by speech and braille.
3. The measurement debt is now the biggest single risk to the stated design
   pillars. Latency is unmeasured, the cross-process buffer-build cost is
   unmeasured, and there is no behavioural regression net.
4. The capability gap needs a scoreboard, and the roadmap needs an explicit
   "never" list.
5. Read mode should target Chromium over native UIA, and the Firefox/IA2 gap
   should be written down now.

The framework that follows from this — what to set up before building resumes —
is in [`FOUNDATION.md`](FOUNDATION.md).

---

## Sources

- [nvaccess/nvda source tree](https://github.com/nvaccess/nvda) — subsystem inventory
- [What's New in NVDA 2026.1](https://download.nvaccess.org/releases/2026.1/documentation/changes.html) — Python 3.13.12, 64-bit only, MathCAT, remote access, add-on store
- [NVDA freezes due to COM object releases triggered by Python garbage collection (#11398)](https://github.com/nvaccess/nvda/issues/11398) — the GC/COM deadlock diagnosis
- [gecko_ia2 vbuf backend: IAccessibleHypertext2 for Firefox multi-process (#7719)](https://github.com/nvaccess/nvda/pull/7719) — cross-process virtual buffer cost
- [nvdaHelperRemote: SendMessageCallback instead of SendMessage (#6380)](https://github.com/nvaccess/nvda/pull/6380) — `RPC_E_CANTCALLOUT_ININPUTSYNCCALL` and why injection exists
- [Fallback to UIA if supported in Chromium and we don't have access to IA2 (#13032)](https://github.com/nvaccess/nvda/pull/13032) — NVDA's browser API selection
- [Why UI Automation is Insufficient as an Accessibility API for the Web](https://www.jantrid.net/2025/03/19/why-uia-insufficient-web/) — Jamie Teh, March 2025
- [Native UI Automation for Windows in Chromium](https://developer.chrome.com/blog/windows-uia-support-update) — Chrome 138 default, Chrome 147 removal of the legacy path
- [Chromium docs: UI Automation](https://chromium.googlesource.com/chromium/src.git/+/refs/heads/main/docs/accessibility/browser/uiautomation.md)
- [Revised approach to addon compatibility (#9055)](https://github.com/nvaccess/nvda/issues/9055) — the annual API-break cycle
- [NVDA Developer Guide](https://download.nvaccess.org/documentation/developerGuide.html) — add-on surface, script resolution
