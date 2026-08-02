# UIAccess, signing, and the secure desktop

Status: **manifests in place; signing pipeline not built; secure desktop not
started.** Background in [`ASSESSMENT.md`](ASSESSMENT.md) S3.

---

## Why this is not optional

A screen reader that runs as an ordinary user-integrity process cannot do three
things, and the third is the one that matters.

**It cannot read elevated windows.** User Interface Privilege Isolation blocks
cross-integrity access, so UIA calls into any admin process are refused.

**It cannot receive its own keystrokes while an elevated window has focus.**
This is the part that surprises people. A `WH_KEYBOARD_LL` hook installed by a
medium-integrity process is *not called* for input delivered to a
high-integrity window. So in Task Manager, `regedit`, an elevated terminal, or
any application whose installer marked it `requireAdministrator`:

- focus announcements stop
- every reader command stops responding
- **the user cannot even press the key that stops speech**

There is no error and no audio. From the user's side the screen reader has
frozen, and the only recovery is a reboot or sighted help.

**It cannot see the secure desktop at all.** UAC consent dialogs,
Ctrl+Alt+Del, the logon screen, and the lock screen run on a separate desktop
in a separate session. No amount of integrity level reaches them; that needs a
second process (below).

For a sighted user each of these is an inconvenience. For the target audience
they are the difference between an accessible machine and a locked one.

---

## What `uiAccess="true"` requires

All three, together. Any one missing and the process **fails to launch** —
it does not fall back to reduced rights.

### 1. An Authenticode signature

The binary must be signed by a certificate that chains to a root in the
machine's Trusted Root store. In practice that means a commercial code-signing
certificate.

- **OV (organisation validation)** is enough for uiAccess. Roughly $200–400/yr.
- **EV (extended validation)** additionally clears SmartScreen reputation
  immediately, which matters a lot for a first release — an unknown installer
  that SmartScreen blocks is an installer a blind user cannot get past.
- Since June 2023 both are issued on hardware tokens or via a cloud HSM.
  A GitHub Actions signing job therefore needs a cloud signing service
  (Azure Trusted Signing, DigiCert KeyLocker, SSL.com eSigner) rather than a
  PFX in a secret. **Budget for this before promising a release date** — it is
  the long pole, and it also gates the auto-updater in roadmap 4f.

Everything shipped must be signed, not just the exe: the host, every
first-party app module DLL, and the MSI.

### 2. Installation under `%ProgramFiles%`

Windows only honours uiAccess for binaries in a trusted location — in practice
`%ProgramFiles%` or `%ProgramFiles(x86)%` (and `%SystemRoot%`). A per-user
install to `%LocalAppData%` **cannot** have uiAccess.

Consequence: the MSI must be a per-machine install, which means it needs
elevation at install time. That is normal and acceptable — the user elevates
once, with sighted help or with Narrator, and then never again.

### 3. The manifest

Already in the tree:

| File | `uiAccess` | Used by |
|---|---|---|
| `src/ReaderHost.Windows/app.manifest` | `false` | `dotnet build` / `dotnet run` — the default |
| `src/ReaderHost.Windows/app.uiaccess.manifest` | `true` | `-p:UiAccess=true` — shipping builds only |

**The split is deliberate and matters for the dev loop.** If the development
manifest requested uiAccess, `dotnet run` on an unsigned build from a source
tree would simply not start, with no useful error. Keep dev builds on
`app.manifest`.

```powershell
# Dev — works from anywhere, unsigned
dotnet run --project src/ReaderHost.Windows

# Shipping — must then be signed and installed to %ProgramFiles%
dotnet publish src/ReaderHost.Windows -c Release -r win-x64 `
    --self-contained false -p:UiAccess=true -o publish/host
```

The build emits a warning when `UiAccess=true` so nobody hand-runs that output
and concludes the exe is broken.

---

## Verifying it actually took effect

Signing silently not applying is the common failure, because everything still
builds.

```powershell
# 1. Is the manifest what you think it is?
mt.exe -inputresource:Aura.exe;#1 -out:extracted.manifest
# look for uiAccess="true"

# 2. Is it signed, and does the chain validate?
signtool verify /pa /v Aura.exe

# 3. Did the process actually get uiAccess?
#    Process Explorer → the process → Properties → Security tab.
#    Integrity should read "Medium Mandatory Level (+UIAccess)".
```

Functional check, in order — each one fails differently:

1. Launch from `%ProgramFiles%`. If the process does not start, signing or
   location is wrong.
2. Focus Task Manager and press the stop-speech key. If it responds, the
   keyboard hook is surviving the integrity boundary.
3. Arrow through Task Manager's process list. If rows announce, UIA is reading
   across the boundary.
4. Trigger a UAC prompt. It will still be silent — that is expected, and is
   the secure desktop problem below.

---

## The secure desktop — a separate, later project

uiAccess does **not** cover the secure desktop. UAC consent, Ctrl+Alt+Del, the
logon screen and the lock screen live on a different desktop (`Winlogon`)
inside session 0's window-station model, and no user-session process can attach
to it.

Covering them requires a second copy of the reader running as SYSTEM with
access to that desktop, plus IPC back to the user-session instance so the user
keeps one voice and one set of preferences. This is what NVDA's "system access"
component does, and it is a genuinely large piece of work: a service, a
separate lifecycle, a privileged surface that has to be hardened because it
runs as SYSTEM and speaks whatever is on screen.

**Recommended sequencing:** ship uiAccess first. It fixes elevated applications,
which is the majority of the day-to-day pain, and it forces the signing
pipeline to exist. Defer the secure desktop until there is a signed release and
a user asking for it.

Interim mitigation worth doing regardless: detect that the foreground desktop
has changed (a UAC prompt is up) and play a distinct earcon plus a spoken "a
security prompt is on screen; press Alt+Y to allow or Escape to cancel". The
reader still cannot read the prompt, but the user is told what happened and how
to get out of it — which is most of the value at a fraction of the cost.

---

## Checklist

- [x] `app.manifest` (dev, `uiAccess="false"`)
- [x] `app.uiaccess.manifest` (shipping, `uiAccess="true"`)
- [x] Manifest selection via `-p:UiAccess=true`, dev-safe by default
- [x] Build warning when `UiAccess=true`
- [ ] Acquire a code-signing certificate (OV minimum; EV recommended)
- [ ] Cloud signing service wired into `release.yml`
- [ ] Sign host exe, app-module DLLs, and MSI
- [ ] MSI switched to per-machine install under `%ProgramFiles%`
- [ ] Verify with `signtool verify /pa` in CI
- [ ] Manual verification pass against Task Manager
- [ ] Desktop-switch detection + spoken UAC hint
- [ ] (later) SYSTEM secure-desktop component
