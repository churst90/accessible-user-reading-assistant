# Capabilities

The scoreboard. Every capability NVDA or JAWS has, one row each, with AURA's
honest status. Reviewed at the end of every phase.

Statuses: **shipped** · **partial** · **planned** · **never** · **undecided**

---

## Why "8 of 45" is the wrong number

`NVDA_ANALYSIS.md` counts NVDA at roughly 45 subsystems and AURA at 8. That
number is true and it is close to useless, because it treats every subsystem as
equally worth having. Three groups make up most of NVDA's count and AURA should
never build any of them:

**Historical necessity (~12).** `displayModel` and its GDI/DirectWrite hooks,
MSAA, `COMRegistrationFixes`, `monkeyPatches`, `_synthDrivers32`, the 32-bit
bridge, Java Access Bridge, Flash and Silverlight remnants, MSHTML. These exist
because NVDA started in 2006 and could not choose its era. AURA starts in a
UIA-first world and every one of these is a liability, not a feature.

**Hardware and ecosystem breadth (~10).** ~50 braille display drivers, FTDI
serial, touch hardware, tactile graphics devices. `DESIGN_PRINCIPLES.md` already
has the right answer: ship what we can verify, and let the community own the
rest through the plugin contract. These are *plugin surface*, not subsystems.

**Genuinely load-bearing for a daily driver (~20).** This is the real
denominator. AURA has 8 of these — so the honest figure is **8 of 20**, and the
8 are the middle of the stack: the abstractions, the rule engine, the text
model, the plugin loader, the arbiter. That is the part that is hardest to
retrofit and the part NVDA can no longer change.

So: **good, structurally — and only if it is a choice.** Uncounted, the other 37
are implicit promises and the project spends its life feeling behind. Counted,
most turn out to be things that *should* never be built, and the remainder is a
finite list. That is what this file is for, and it is why the **never** column
does more work here than any other.

---

## Content access

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| UI Automation | yes | **shipped** | Native `IUIAutomation` via CsWin32; cache request; coalescing |
| MSAA / IAccessible | yes | **never** | Superseded by UIA for everything AURA targets |
| IAccessible2 | yes | **undecided** | Only reason: Firefox. See R5. A second `IAccessibilityProvider`, not a patch |
| Java Access Bridge | yes | **never** | No user has asked. Revisit only if one does |
| Chromium web content | via IA2 | **planned** (4c) | Native UIA is viable from Chrome 138 |
| Firefox web content | via IA2 | **never** *(pending)* | Blocked on the IA2 decision above; say so rather than implying support |
| Windows console / terminal | yes | **planned** (F3) | Known broken today; the first tree interceptor |
| MS Word / Excel / Outlook via COM | yes | **undecided** | Huge value, huge surface. Needs a named user before starting |
| PDF / Acrobat | yes | **undecided** | Chromium's PDF viewer may cover most real use via UIA |
| Display-model text capture | yes | **never** | The AV-false-positive and injection cost is not payable. Roadmap 4h already says defer indefinitely |
| OCR fallback | yes | **planned** (4e) | `Windows.Media.Ocr` |

## Document structure and navigation

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| Text range model | `TextInfo` | **shipped** | `ITextRange`/`ITextSurface`; the largest thing banked |
| Structured text (field commands) | yes | **planned** (F2) | The contract hole that blocks Read mode |
| Review cursor | yes | **shipped** | Follows the caret; is an `ITextRange` |
| Object navigation | yes | **planned** (F3) | A missing *axis*. Among the first things a switching user reaches for |
| Say all | yes | **partial** | Works; no resume-from-position, no reading-chunk pacing (needs F1 markers) |
| Browse / Read mode | yes | **planned** (4c) | Gated on F2, F3 and the R2 measurement |
| Quick navigation (single letters) | yes | **planned** (4c) | Contracts exist (`NavigationTarget`) |
| Elements list (links/headings/…) | yes | **planned** | Falls out of F2 + 4c; cheap once both exist |
| Table navigation | yes | **planned** (F2) | Ctrl+Alt+arrows. Top-tier feature, previously unlisted |
| Table headers | yes | **planned** (F2) | Open question: trust `TablePattern` or infer |
| Landmark navigation | yes | **planned** (4c) | `NavigationTarget.Landmark` exists |
| Math (MathCAT/MathML) | yes | **never** *(pending)* | Real users need it; not before a daily driver exists |
| Annotations / revisions | yes | **undecided** | Needs F2 first |
| Spelling / grammar reporting | yes | **planned** (F2) | `TextAttributes` already defines the keys |

## Speech output

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| SAPI 5 | yes | **shipped** | |
| eSpeak NG | yes | **shipped** | Requires user install; bundling is a follow-up |
| OneCore / Windows voices | yes | **planned** | Cheap after F1 |
| Neural TTS (Piper etc.) | add-on | **planned** | A differentiator; needs F1's streaming/marker model |
| Speech as a sequence | yes | **shipped** (F1) | `Utterance` of `OutputPart`s. Was the largest contract hole |
| Per-language voice switching | yes | **partial** | `LanguagePart` exists and renders; no per-language voice arbitration yet (F7) |
| Prosody spans (capitals by pitch) | yes | **shipped** (F1) | `ProsodyPush`/`Pop` as a stack, so an interrupted utterance re-establishes state on resume |
| Inline earcons | yes | **partial** | `CuePart` is carried end-to-end; nothing plays it until a theme exists (4b) |
| Say-all position markers | yes | **partial** | `MarkerPart` + `ISpeechEngine.MarkerReached` ship; say-all does not resume from one yet |
| Verbosity levels (what to omit) | yes | **shipped** | Filtered per `SegmentKind` — role / position / state / description / hints. Deliberately no switch for the name or the text |
| Data-driven speech rules | **no** | **shipped** | AURA is *ahead*. Users ask NVDA for this and cannot have it |
| Rule trace ("why did it say that?") | **no** | **partial** | Recorded, never surfaced. Would be a first |
| Symbol level / dictionaries | yes | **planned** (F7) | Engine exists, data does not |
| Character descriptions | yes | **planned** (F7) | |
| User speech dictionaries | yes | **planned** (F7) | |
| Speech viewer | yes | **planned** | Cheap after F1's `TranscriptRenderer` — same renderer |

## Braille

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| liblouis translation | yes | **planned** (4g) | |
| Braille as a presentation renderer | **no** — parallel path | **planned** (F1) | Where AURA gets to be structurally better |
| Braille viewer | yes | **planned** (4g) | **Ship before any hardware** — it validates the renderer |
| Display drivers | ~50 | **planned**: 1 | One we can physically test; community owns the rest via the contract |
| Braille input | yes | **undecided** | Needs a display and a user |
| Tethering (focus/review/auto) | yes | **planned** (4g) | |

## Audio

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| Audio themes / earcons | yes | **planned** (4b) | Sine bells, not square waves |
| Mixer | yes | **planned** (4b) | |
| Ducking | yes | **planned** (4b) | Off by default |
| Sound split / spatial cues | partial | **undecided** | A genuine differentiation opportunity |

## Input

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| Keyboard hook + gesture model | yes | **shipped** | Context-layered; documented with drift tests |
| Per-object / interceptor bindings | yes | **planned** (F3) | The chain is incomplete today |
| Rebinding UI | yes | **shipped** | |
| Input help mode | yes | **planned** | Cheap; its absence is conspicuous |
| Touch gestures | yes | **never** *(pending)* | No user has asked |
| Braille input | yes | **undecided** | With braille |

## System integration

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| uiAccess (elevated windows) | yes | **partial** | Manifests written; **certificate not obtained** — R3, the long pole |
| Secure desktop / logon screen | yes | **planned** | A SYSTEM instance. After the certificate |
| Portable copy | yes | **shipped** | Self-contained zip |
| MSI installer | yes | **shipped** | WiX 4; release workflow unproven |
| Auto-update | yes | **planned** (4f) | After 2–3 trusted manual releases |
| Remote access | yes (built in) | **planned** (4i) | As plugins, after F6 |
| Screen curtain | yes | **undecided** | Cheap (Magnification API), and genuinely wanted by some users |
| Vision enhancement providers | yes | **never** | Magnification is a different product |

## Extensibility and ecosystem

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| Versioned plugin contract | partial | **shipped** | AURA is ahead: NVDA breaks add-ons annually |
| Plugin isolation (unloadable) | no | **shipped** | Collectible ALC |
| Extension points (Action/Filter/Decider) | yes | **planned** (F6) | |
| App modules | yes | **partial** | Four scaffolds, not integrations |
| SDK + project template | no | **shipped** | AURA is ahead |
| Add-on store | yes | **undecided** | Needs an ecosystem first. Do not build the shop before the goods |
| Deprecation policy | **no** | **planned** (F6) | NVDA's loudest ecosystem complaint |
| Out-of-process untrusted plugins | no | **undecided** | Viable for filters, not for text-path plugins (F6) |

## Diagnostics and quality

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| Structured logging | partial | **shipped** | With content redaction on by default |
| Log viewer in-app | yes | **planned** | |
| Speech viewer | yes | **planned** | F1 |
| Golden transcript regression tests | **no** | **shipped** (F5a) | **Nobody has this.** 14 scenarios; each one is a bug that cannot come back. See the caveat below |
| Backend conformance suites | no | **planned** (F5b) | The platform layer has *no* coverage today — see the caveat below. This is the highest-value untested surface |
| Measured latency budgets | no | **planned** (F5c) | Principle stated since day one, never measured |
| Freeze watchdog | yes | **partial** | Beeps; does not recover. F4d |
| Deterministic COM lifetime | workaround | **shipped** (F4b) | AURA is *ahead*. NVDA disables automatic GC and collects at one point in the main loop, after issue #11398; AURA owns every reference explicitly and drains a release queue at that same point, by design rather than after the bug hunt |
| Crash recovery | yes | **partial** | Global handlers exist; no provider rebuild |

## Localisation

| Capability | NVDA | AURA | Notes |
|---|---|---|---|
| UI translations | 60+ locales | **never** *(pending)* | Not before a stable UI and a user asking |
| Symbol dictionaries per locale | yes | **planned** (F7) | `en` only |
| Braille tables | yes | **planned** (4g) | liblouis brings them |
| Translated documentation | yes | **never** *(pending)* | |

---

## Where AURA is already ahead

Worth keeping visible, because a scoreboard that only counts gaps is
demoralising and also wrong:

1. **Data-driven speech rules**, layered by user/profile/app. The thing NVDA
   users most often ask for and cannot have.
2. **A versioned, isolated, dogfooded plugin contract** with an SDK and a
   `dotnet new` template — before v1.0.
3. **A real text model from month two**, rather than one grown from offsets.
4. **A synthetic accessibility tree** that makes core logic testable with no
   Windows and no applications.
5. **Multi-threaded by design**, rather than a cooperative single thread with a
   watchdog compensating for it.
6. **A rule trace** — the raw material for a screen reader that can explain its
   own output, which nothing else does.

---

## Counting

| Status | Count |
|---|---|
| shipped | 18 |
| partial | 11 |
| planned | 32 |
| undecided | 12 |
| never | 12 |

The number to watch is **undecided**. Each one is a question that will be
answered by drift if it is not answered deliberately, and drift always answers
"yes, eventually, badly".

---

## The caveat this file needs, as of 2026-08-06

**"Shipped" means the contract exists and has tests. It does not mean a blind
user has heard it work.** Three rounds of listening on real hardware in early
August found thirteen behavioural bugs across capabilities all marked shipped —
lists announcing nothing, items announced twice, Delete never firing. Every one
was audible within seconds and invisible to a green suite.

The golden transcripts (F5a) are the answer to that and they are already
earning their place, but round 3 also found their limit: the harness held its
own copy of the host's announcement policy, so when the host asked the wrong
question the harness asked the same wrong question and reported success. A
regression suite that duplicates the logic it tests agrees with the bug. That
is F5 open question 1, and it is now the most valuable unfixed thing here.

So read this scoreboard as *what has been built*, and
[`SESSION_HANDOFF.md`](SESSION_HANDOFF.md) as *what has been heard*. Until a
daily driver exists, the second number is the real one.

**And there is a reason those rounds found so much.** CI ran the Windows-only
suite on 2026-08-12 for the first time in a long while:
`ReaderPlatform.Windows.Tests` contains **five tests, four of which are prosody
arithmetic**, and the fifth — named
`UiaProvider_translates_focus_event_to_AccessibilityEvent` — is a placeholder
skipped since Phase 1 that has stayed skipped while the provider underneath it
was written, migrated to native COM, and rewritten.

Which means `NativeUiaProvider`, `NativeUiaNodeMapper`, `UiaTextSurface`,
`Win32TextSurface`, both speech engines and the keyboard hook have **no test
coverage at all**. Every round-3 bug lived in exactly that layer — node
identity, desktop item counts, the combo-box selection fallback, the ungated
pattern call on the focus path. The platform layer is where the hardware bugs
are because it is the layer nothing checks. That is what **F5b (backend
conformance suites)** is for, and this is its justification.
