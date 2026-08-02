# MyPlugin

An Aura app module scaffolded by `dotnet new aura-plugin`.

## Build

```pwsh
dotnet build -c Release
```

## Install

Drop the contents of `bin/Release/netstandard2.1/` into a folder under
`%AppData%\Aura\plugins\<your-plugin-id>\` and (re)start Aura.

OR set `AURA_DEV=1` before launching Aura and the host will
hot-reload your plugin folder on every change.

## Verify

Focus the application your plugin matches; you should hear the
`OnAttachAsync` announcement. Use `Insert+1` to enter keyboard help mode if
you need to confirm key bindings aren't getting in your way.

## API surface

The whole contract lives in `Aura.Abstractions.Plugins`:

- `IAppModule` — implement on a parameterless-constructible class.
- `IAppContext` — what the host hands you on attach: process info,
  accessibility provider, `AnnounceAsync`, `RegisterSpeechRule`.
- `AppModuleManifest` — declared API version is enforced by the host on
  load.

See [the Aura docs](https://github.com/Aura/Aura/blob/main/docs/ARCHITECTURE.md#plugin-host-scripting) for the complete contract.
