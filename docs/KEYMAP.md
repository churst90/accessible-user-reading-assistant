# Default key bindings

`Reader` = Insert (desktop layout) or CapsLock (laptop layout).

## Reading and review

| Chord                           | Action                                        |
|---------------------------------|-----------------------------------------------|
| `Reader+F`                      | Report focused control                         |
| `Reader+T`                      | Read window title                              |
| `Reader+F12`                    | Speak system time (double-press: speak date)   |
| `Reader+A`                      | Say all from review cursor                     |
| `Reader+P`                      | Cycle punctuation level (None / Some / Most / All) |
| `Reader+Arrow`                  | Char / line review (laptop + desktop both)     |
| `Reader+Ctrl+Arrow`             | Word review                                    |
| `Reader+.`                      | Sync review cursor to focus                    |
| `Reader+Ctrl+Home / End`        | Review top / bottom (laptop)                   |

## Numpad review (desktop layout, NumLock ON)

```
  7  8  9       Top   PrevLine   -
  4  5  6   PrevChar  CurChar    NextChar
  1  2  3       End   NextLine   -
```
`Ctrl+Numpad 4 / 6` = previous / next word. Numpad keys are intercepted so
they review without inserting digits or moving the system caret.

## Modes and meta

| Chord                           | Action                                        |
|---------------------------------|-----------------------------------------------|
| `Reader+1`                      | Toggle keyboard help mode (any chord names itself; `Reader+1` or `Ctrl` exits) |
| `Reader+O`                      | Open settings                                  |
| `Reader+Q`                      | Open exit dialog (Yes / No)                    |
| `Reader+F1`                     | Open documentation                             |
| `Ctrl` (alone)                  | Stop speech (observed; passes through)         |
| `CapsLock` (double-tap)         | Toggle screen reader on / off (laptop layout)  |
| `CapsLock` (hold)               | Use as Reader modifier (laptop layout)         |

## Notes on CapsLock

In laptop layout, CapsLock is a hybrid:

- **Hold + another key** → Reader modifier (e.g. `CapsLock+Down` = next line).
- **Solo tap** → no-op for the OS (CapsLock toggle suppressed). Two solo
  taps within ~450 ms toggle the screen reader as a whole (`ToggleEnabled`).
- **Single solo tap** alone does nothing visible — it's the half of the
  double-tap gesture.

In desktop layout CapsLock is *not* the Reader modifier and behaves like a
normal CapsLock key.
