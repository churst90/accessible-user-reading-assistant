# Extensions, maintainability, and interop

Three questions that turn out to be the same question: **what belongs in core,
and what belongs outside it?**

---

## The maintainability rule

Joanie Diggs' framing of Orca is the right one to steal: *a screen reader
should be maintainable by the people who maintain it, and it should not be
carrying code whose real owner is somewhere else.*

That sounds obvious and is violated constantly, because the pressure runs the
other way. An application has an accessibility bug. Fixing it upstream takes
months and might never happen. Working around it in the screen reader takes an
afternoon and the user is happy tomorrow. So the workaround lands — and now it
is yours forever, because nobody remembers which app version it was for, and
removing it risks breaking a user you cannot contact.

Do that a few hundred times over twenty years and you get a codebase where
every change might break something invisible. That is a large part of why NVDA
is in the state it is in, and none of it was a bad decision at the time.

**Three rules, and a mechanism for each.**

### 1. Application quirks never live in core

Not in the dispatch loop, not in the speech pipeline, not behind an
`if (appName == ...)`. They live in app modules, which are versioned, isolated,
and removable. This is already enforced structurally — `Reader.Core` cannot
see a UIA type, let alone an application name.

The test: *could I delete every app module and still have a working screen
reader?* If no, something leaked.

### 2. Every workaround declares why it exists and when it dies

A workaround with no expiry is a workaround forever. App module manifests
should carry this, and the field should be required rather than optional:

```json
{
  "id": "openreader.appmodule.example",
  "quirks": [
    {
      "describes": "Reports stale value on focus for a moment after paint",
      "affects": "Example.exe < 4.2",
      "upstream": "https://github.com/example/example/issues/1234",
      "removeWhen": "4.2 is the oldest version we support"
    }
  ]
}
```

None of that is enforceable by a compiler. What it does is make the debt
*visible*: someone can list every workaround in the tree, check which upstream
bugs are fixed, and delete with confidence. Today's answer to "can I remove
this?" is a shrug, and a shrug means it stays.

### 3. Dependencies stay dependencies

Do not vendor. Do not fork and patch. If eSpeak NG or liblouis has a bug, fix
it upstream and pin a version until it lands. The moment a copy is edited in
this tree, its maintenance is ours and its upstream fixes stop arriving.

The corollary, from `DESIGN_PRINCIPLES.md`: **no feature without an owner who
can test it.** Support for a braille display nobody on the project owns is not
support, it is a liability wearing support's clothes.

---

## The extension model

Extensions are called **app modules** when they adapt a specific application,
and **plugins** generally. NVDA calls them add-ons; Orca calls them extensions;
the concept is the same.

**What exists today** (Phase 3, shipped): a versioned `IAppModule` contract,
per-plugin collectible `AssemblyLoadContext`, manifest validation with an API
version gate, hot reload in dev, an `OpenReader.Sdk` NuGet package, and a
`dotnet new` template. First-party modules go through the *same* loader as
third-party ones — the contract is dogfooded rather than asserted.

**What is missing** (Phase 4d) — and the ordering matters, because each unlocks
the next:

| Contract | Unlocks |
|---|---|
| `IPluginCommand` | Any plugin that needs a keystroke. Required before the rest are useful. |
| `ISettingsPanel` | A plugin that has settings and nowhere to put them |
| `IAudioTheme` | Sound packs (4b) |
| `ISpeechEngine` promoted to the SDK | Third-party synthesisers — Azure, ElevenLabs, Piper — without forking |
| `IInputSource` promoted to the SDK | Braille input, touch, remote input |
| `IReadModeProvider` | Read mode for a document type we don't handle |
| Lifecycle hooks | `OnStartup`, `OnShutdown`, `OnProfileChanged` |

### Say the trust model out loud

Plugins run **in-process at full trust**. The load context isolates *type
identity* so a plugin can be unloaded and so its `IAppModule` is the same type
the host references. It is **not** a security boundary. A plugin can read your
files, open sockets, P/Invoke, and reflect into the host.

The manifest `capabilities` field documents intent. It does not constrain
anything, and calling it a "grant" would be a lie that users make installation
decisions on. Real enforcement needs a process boundary per plugin, with real
latency cost on the hot path — worth scoping honestly before promising.

Until then the position is NVDA's, stated plainly: **install only add-ons you
trust.**

---

## Can we reuse NVDA Remote?

Short answer: **reuse the protocol, not the code.** Yes to interoperating, no
to importing.

### Why not the code

NVDA is GPLv2 and the NVDA Remote add-on is GPLv2. OpenReader is MIT. Those do
not mix: GPL is copyleft, so a derivative work has to be GPL too. Importing
NVDA Remote's source would relicense whatever it is linked into.

That is a licensing constraint, not a quality judgement — NVDA Remote is good
software and it solved this problem first. But it is a fork in the road that
has to be taken deliberately:

- **Stay MIT** and clean-room the protocol, or
- **Adopt GPL** for the whole project and inherit the ecosystem

Worth deciding early, because it is very expensive to reverse once contributors
have signed on under one of them. *Get an actual lawyer to confirm before
shipping anything that touches this — the summary above is engineering
reasoning, not legal advice.*

### Why the protocol is still worth having

Wire protocols are not copyrightable the way source code is, and implementing
one from its observable behaviour is ordinary practice. What that buys is
disproportionate:

- An OpenReader user can get remote support **from an NVDA user**, and vice
  versa. On day one, with no new infrastructure.
- Existing relay servers work, including community ones people already trust.
- Nobody has to run a server to try it.

For a project whose entire pitch is "a robust alternative to NVDA", being able
to interoperate with NVDA rather than demanding users abandon it is close to a
requirement. The realistic adoption path is *both installed for a while*, and
anything that punishes that path costs users.

Note that NV Access has been moving remote access into NVDA core rather than
leaving it an add-on, which makes the protocol more of a de-facto standard and
raises the value of speaking it. **Check the current state before designing
against it** — this moves, and it may have moved since this was written.

### Shape

`ASSESSMENT.md` and roadmap 4i already argue for event mirroring over audio
streaming: lower bandwidth, and the local user keeps their own voice, rate and
prosody, which matters more than it sounds. A remote session that speaks in
someone else's voice at someone else's rate is exhausting.

Build it as **plugins** (`OpenReader.Relay.Server` / `.Client`), not host code.
That forces the 4d contract widening to be genuinely sufficient — if the relay
cannot be written as a plugin, the plugin API is not finished, and better to
learn that from our own code than from a frustrated third party.

Sequence: 4d contracts → relay as a plugin → protocol compatibility as a
separate, testable layer. Do not start with compatibility; start with something
that works, then make it speak the other protocol.

---

## The rule of thumb

Before adding anything to core, ask: **if this were wrong, who would fix it,
and could they?**

If the answer is "whoever inherits this file in three years, and only by
guessing" — it belongs in a plugin, behind a contract, with its reason written
down.
