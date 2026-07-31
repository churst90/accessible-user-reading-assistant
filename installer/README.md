# OpenReader installer

WiX 4 project. **Not** part of `OpenReader.slnx` — it requires the WiX
global tool, so we keep the main solution buildable without it.

## Prereqs

```pwsh
dotnet tool install --global wix --version 4.0.5
```

## Local build

```pwsh
# 1. Publish the host (framework-dependent, x64).
dotnet publish src/ReaderHost.Windows -c Release -r win-x64 `
    --self-contained false -o publish/host

# 2. Regenerate the harvested file fragment.
pwsh scripts/regenerate-installer-files.ps1

# 3. Build the MSI.
dotnet build installer/OpenReader.Installer.wixproj -c Release `
    -p:ProductVersion=0.1.0
```

The MSI lands in `installer/bin/x64/Release/OpenReader-0.1.0.msi`.

## What the installer does

- Drops the host into `%ProgramFiles%\OpenReader\`.
- Creates a Start Menu shortcut.
- Registers a major-upgrade rule (downgrades are blocked with a clear
  error message).
- **Does NOT** write the auto-start key. That's user-controlled at
  runtime via the General settings panel — installing the app should
  not change a user's login experience without their consent.

## Release flow

Tag a release as `vX.Y.Z`; CI runs `release.yml` (see
`.github/workflows/release.yml`) which:

1. Builds and tests the solution.
2. Publishes the host.
3. Regenerates `HostFilesGenerated.wxi`.
4. Builds the MSI with `ProductVersion` derived from the tag.
5. Uploads the MSI as a workflow artifact and attaches it to the
   GitHub Release.

Updates are out of scope for v0.1; users download the new MSI manually.
