# Testing Aura

For the first people trying this. Read the whole page before you start — a
couple of things below will otherwise look like bugs.

**This is pre-alpha.** It has never been used for real work by anyone. Expect
it to be wrong, not to be finished.

---

## Before you start

**Do not use this as your only screen reader.** Keep NVDA or Narrator running
and know how to switch back. If Aura stops speaking you need a way to
recover that does not depend on Aura.

Requirements:

- Windows 10 1809 or later, x64 or ARM64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional: [eSpeak NG](https://github.com/espeak-ng/espeak-ng/releases) for a
  second synthesiser. Without it you get SAPI 5 only, silently.

**SmartScreen will block it.** The build is not code-signed. You will get
"Windows protected your PC" and have to choose "More info" then "Run anyway".
That warning is legitimate — it means nobody has vouched for this binary. Only
do it because you know where it came from.

```
dotnet run --project src/ReaderHost.Windows
```

Press **Reader+Q** (Insert+Q or CapsLock+Q) to exit. If it stops responding
entirely, kill `Aura.exe` from Task Manager — though see the known gap
about elevated windows below, because Task Manager is one of them.

---

## Reporting a problem

**Press Ctrl+Reader+D.** That copies a diagnostic snapshot to the clipboard —
version, synthesiser, voice, rate, keyboard layout, the focused application,
and the log location. Paste it into the report.

It deliberately contains **no spoken text and no control content**. A screen
reader reads banking pages and password managers aloud; none of that belongs in
a bug report. If you need to show what was announced, describe it in your own
words.

Logs are at `%LocalAppData%\Aura\logs\aura-<date>.log`. They are
content-redacted by default. If you are asked to turn redaction off for a
specific reproduction, set `Diagnostics.RedactContent` to `false` in
`%AppData%\Aura\config.json`, reproduce, then **turn it back on** and
check the log before sending it.

A good report is: what you did, what you expected to hear, what you actually
heard, and the diagnostic snapshot.

---

## What to try, hardest first

The caret and text handling were rewritten from scratch and have never been
heard by anyone. That is where the bugs are. Please spend your time here rather
than on the settings dialog.

### 1. Arrowing through text — the riskiest area

Open Notepad, type a few lines, then:

- Left and right arrow — should read the character you land on
- Ctrl+left and ctrl+right — should read the word
- Up and down — should read the line
- Home and End
- **Left arrow at the start of a line** — should read the line above, not a
  character. This one is worth checking specifically; it is a case the old
  implementation got wrong.
- Shift+arrows — should say what got selected, and "unselected" when it shrinks
- Type an emoji and arrow over it — should be one character, not two noises

Then repeat all of it in WordPad, a browser address bar, and a text field on a
web page. They use different underlying mechanisms and may behave differently.

### 2. Typing echo

- Type `don't` and `well-known` — should say "don't" and "well-known", **not**
  "don" "t" and "well" "known"
- Press backspace while typing — should say the character it removed
- **Known gap:** backspace says only "backspace" if you arrowed into existing
  text rather than typing it. Expected for now.

### 3. Focus announcements

- Tab through a dialog
- Win+R, type — the Run box should read sensibly, not re-read its whole
  contents on every keystroke
- **Arrow along a toolbar of unlabelled icon buttons.** Every button should be
  announced. Silence after the first one is a bug we specifically fixed and
  want to know if it came back.
- Arrow through a list or a table column where several rows say the same thing
  — each move should announce

### 4. Lists, dialogs and alerts

- Arrow through a listbox — selection should be announced
- A dialog that opens without taking focus
- Tooltips, menus

### 5. Does it survive?

- Leave it running for an hour while you work
- Open something heavy — Teams, Slack, VS Code, a big web page
- **Freeze an app deliberately** and keep pressing keys. You should hear a beep
  after about two seconds telling you the reader is stalled, and a lower beep
  when it recovers. Silence with no beep is the worst possible outcome and we
  want to know immediately.

---

## Known gaps — not worth reporting

These are missing on purpose. Reporting them costs you time and tells us
nothing we don't know.

| Gap | Why |
|---|---|
| Elevated apps are silent and unresponsive — Task Manager, regedit, admin terminals | Needs a code-signing certificate and uiAccess. See `docs/UIACCESS.md`. **The keyboard hook stops entirely**, so reader commands do nothing there. |
| UAC prompts, Ctrl+Alt+Del, the logon and lock screens | Separate desktop; needs a SYSTEM-level component that does not exist |
| No web Read mode — web pages read as a flat control list | Phase 4c. Contracts sketched, not built. |
| No braille | Phase 4g, deferred until someone with a display asks |
| No sounds or earcons other than the stall beep | Phase 4b |
| Live regions and toast notifications are silent | The managed UIA API has no event for them; needs the COM migration |
| eSpeak NG absent → SAPI only, with no announcement | Known; should say so |
| No auto-update | Phase 4f, after a signed release exists |

---

## What is genuinely useful to report

- Anything that makes it **stop speaking**. Top priority, always.
- Reading the wrong thing — especially the wrong line or the wrong character
- Announcing something twice, or not at all
- An application where it behaves noticeably worse than NVDA. Name the app and
  the version.
- Anything that feels slow. "Sluggish" is a real report; we have a latency
  target and no measurements yet.
- Crashes — the log will have a stack trace

Two things worth saying plainly: we would rather have ten reports of the same
issue than none, and "this feels wrong but I can't say why" is worth sending.
The people who notice a screen reader is subtly off are usually right well
before they can articulate it.
