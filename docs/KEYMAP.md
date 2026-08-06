# Default key bindings

**`Reader`** = **Insert** (desktop layout) or **CapsLock** (laptop layout).
Configurable via `Input.ReaderModifier` — `insert`, `capslock`, or `both`
(the default is `both`, so either key works).

This page is checked against the code by `KeymapDocumentationTests`. If you add
a binding and do not document it, that test fails.

---

## Everywhere (both layouts)

| Chord | Action |
|---|---|
| `Ctrl` alone | Stop speech — observed, still passes through to the app |
| `Reader+Tab` | Report focused control |
| `Reader+F` | Report focused control |
| `Reader+T` | Report title — the window title, not the focused control |
| `Reader+F12` | Speak time. Press twice quickly for the date. |
| `Reader+L` | Read current line |
| `Reader+A` | Say all from the review cursor |
| `Reader+P` | Cycle punctuation level — None / Some / Most / All |
| `Reader+1` | Toggle keyboard help mode |
| `Reader+F1` | Open documentation |
| `Reader+N` | Open settings |
| `Reader+A` | Open the Aura menu (settings, docs, exit) |
| `Reader+Q` | Exit dialog |
| `Ctrl+Reader+S` | Choose synthesiser |
| `Ctrl+Reader+D` | **Copy diagnostics to the clipboard** — use this in bug reports |
| `Reader+Space` | Switch between Read mode and Write mode (same key as NVDA) |

---

## Desktop layout — numpad review (NumLock ON)

```
 7 prev line    8 current line    9 next line
 4 prev word    5 current word    6 next word
 1 prev char    2 current char    3 next char
```

| Chord | Action |
|---|---|
| `Shift+Numpad7` | Review to top |
| `Shift+Numpad1` | Review to bottom |
| `Reader+Numpad+` | Say all from cursor |
| `Reader+Down` | Say all from cursor |
| `Reader+Shift+Down` | Say all from the beginning |
| `Reader+Up` | Read current line |
| `Reader+Numpad.` | Move review to focus |
| `Reader+.` | Move review to focus |

Numpad keys are intercepted, so they review instead of typing digits.

---

## Laptop layout — main arrow cluster

| Chord | Action |
|---|---|
| `Reader+Left` / `Reader+Right` | Previous / next character |
| `Reader+.` | Current character |
| `Ctrl+Reader+Left` / `Ctrl+Reader+Right` | Previous / next word |
| `Ctrl+Reader+.` | Current word |
| `Reader+Up` / `Reader+Down` | Previous / next line |
| `Shift+Reader+.` | Current line |
| `Shift+Reader+A` | Say all from cursor |
| `Ctrl+Shift+Reader+A` | Say all from the beginning |
| `Shift+Reader+Home` | Review to top |
| `Shift+Reader+End` | Review to bottom |

---

## Not on the keyboard

| Action | How |
|---|---|
| Toggle the screen reader on/off | Double-tap CapsLock, or the tray menu |
| Speak the date | Press `Reader+F12` twice quickly |

---

## CapsLock behaviour

CapsLock is a hybrid when it is the Reader modifier:

- **Held with another key** — acts as the Reader modifier
- **Tapped once alone** — nothing; the OS CapsLock toggle is suppressed
- **Tapped twice quickly** (~450 ms) — toggles the screen reader on and off

When `Input.ReaderModifier` is `insert`, CapsLock behaves like a normal
CapsLock key.

---

## Speech echo settings

Settings → Keyboard. Four independent toggles:

| Setting | Default | Speaks |
|---|---|---|
| Command keys | **off** | Named keys: Control, Alt, Windows, Shift, CapsLock, Tab, Escape, Enter, Backspace, Delete, arrows, function keys |
| Each typed character | off | Every printable character as you type it |
| Each completed word | **on** | The word, at a space or sentence punctuation |
| Character removed by Backspace/Delete | **on** | The character a deletion removed |
| Apply echo in Read mode | off | Extends the two echoes above into Read mode |

Character and word echo are **independent checkboxes** and both may be on at
once. NVDA presents these as a single four-way choice (off / characters /
words / both); those four options are just the combinations of two booleans,
and listing them makes you translate what you want into someone else's
enumeration.

**Apply echo in Read mode** is off by default because in Read mode a single
letter is a *command*, not text — `h` jumps to the next heading, and echoing
"h" before the heading is noise. Turn it on if you navigate mostly by
quick-key and want confirmation the key registered. It does not affect
deletion echo: deleting is destructive in either mode.

Two deliberate asymmetries:

**With "command keys" off, no key name is ever spoken** — not "backspace",
not "tab", under any circumstance. That is an invariant with a test on it.

**Deletion echo is independent of character echo.** Deleting is destructive and
cannot be verified any other way, so a user who finds per-character echo too
chatty still hears what vanished. The removed character is content, not a key
name, so "command keys" off does not silence it either.

## Rebinding

Settings → Key bindings, or edit `%AppData%\Aura\config.json`:

```json
{
  "Input": {
    "KeyBindings": {
      "Reader+Shift+L": "ReadLine",
      "Ctrl+Alt+P": "CyclePunctuationLevel"
    }
  }
}
```

Chord syntax is modifiers plus a key, joined by `+`, in any order and
case-insensitive: `Reader`, `Ctrl`/`Control`, `Shift`, `Alt`, `Win`. Changes
apply on save — no restart.

User bindings live in their own layer and override the built-in defaults
without erasing them, so removing a rebinding restores the original.

---

## Command reference

The exact names to use in `Input.KeyBindings`. Every command is listed,
including the two with no default chord.

| Command | Does | Default chord |
|---|---|---|
| `StopSpeech` | Stop speaking immediately | `Ctrl` |
| `SayAll` | Read from the beginning | `Reader+Shift+Down` (desktop), `Ctrl+Shift+Reader+A` (laptop) |
| `SayAllFromCursor` | Read from the review cursor | `Reader+A`, `Reader+Numpad+`, `Reader+Down` |
| `ReadCharacter` | Speak the character at the review cursor | `Numpad2` / `Reader+.` |
| `ReadNextCharacter` | Move right one character and speak it | `Numpad3` / `Reader+Right` |
| `ReadPreviousCharacter` | Move left one character and speak it | `Numpad1` / `Reader+Left` |
| `ReadWord` | Speak the word at the review cursor | `Numpad5` / `Ctrl+Reader+.` |
| `ReadNextWord` | Move to the next word and speak it | `Numpad6` / `Ctrl+Reader+Right` |
| `ReadPreviousWord` | Move to the previous word and speak it | `Numpad4` / `Ctrl+Reader+Left` |
| `ReadLine` | Speak the line at the review cursor | `Numpad8`, `Reader+L`, `Reader+Up` / `Shift+Reader+.` |
| `ReadNextLine` | Move down one line and speak it | `Numpad9` / `Reader+Down` |
| `ReadPreviousLine` | Move up one line and speak it | `Numpad7` / `Reader+Up` |
| `ReviewMoveToTop` | Review cursor to the start | `Shift+Numpad7` / `Shift+Reader+Home` |
| `ReviewMoveToBottom` | Review cursor to the end | `Shift+Numpad1` / `Shift+Reader+End` |
| `ReviewMoveToFocus` | Review cursor back to the focused control | `Reader+.`, `Reader+Numpad.` |
| `ReportFocus` | Describe the focused control | `Reader+Tab`, `Reader+F` |
| `ReportTitle` | Speak the window title | `Reader+T` |
| `ReportTime` | Speak the time | `Reader+F12` |
| `ReportDate` | Speak the date | *(none — double-tap `Reader+F12`)* |
| `CyclePunctuationLevel` | Cycle punctuation verbosity | `Reader+P` |
| `ToggleKeyboardHelp` | Announce keys instead of running them | `Reader+1` |
| `ToggleEnabled` | Turn the reader on or off | *(none — double-tap CapsLock, or the tray)* |
| `OpenSettings` | Open the settings window | (from the Aura menu) |
| `OpenAuraMenu` | Open the Aura menu | `Reader+A` |
| `OpenDocumentation` | Open the documentation | `Reader+F1` |
| `OpenExitDialog` | Open the exit confirmation | `Reader+Q` |
| `OpenSynthesizerDialog` | Choose the speech synthesiser | `Ctrl+Reader+S` |
| `ReportDiagnostics` | Copy a diagnostic snapshot to the clipboard | `Ctrl+Reader+D` |
| `ToggleReaderMode` | Switch between Read mode and Write mode | `Reader+Space` |

---

## Layers

Bindings resolve through context-scoped layers, most specific first:

| Layer | Priority | Active when |
|---|---|---|
| `readmode` | 300 | Reading a document (Phase 4c) |
| `app:<exe>` | 200 | That application is focused |
| `user` | 100 | Always — your rebindings |
| `default` | 0 | Always — the tables above |

This is what lets Read mode bind bare `h` to "next heading" without breaking
the letter `h` everywhere else. Nothing populates `readmode` yet; see
[`READ_WRITE_MODES.md`](READ_WRITE_MODES.md).
