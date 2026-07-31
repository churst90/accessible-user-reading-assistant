# Sample plugin: Edge tab announcer

A 30-line OpenReader app module that announces "tab changed in Edge" on
every tab-focus event in Microsoft Edge. This is the proof point named in
the [roadmap](../../docs/ROADMAP.md#phase-3--extensibility).

## What it shows

- Implementing `IAppModule` end-to-end.
- Filtering by executable name in `Matches`.
- Registering a `SpeechRule` on attach and disposing the handle on detach.
- Using a `SpeechRuleScope` to scope the rule to a role + app + event reason.

## Try it

```pwsh
# 1. Build the sample.
dotnet build samples/SamplePlugin -c Release

# 2. Copy into your plugins folder.
$dest = "$env:APPDATA\OpenReader\plugins\openreader.sample.tab-announcer"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item -Force `
    samples/SamplePlugin/bin/Release/netstandard2.1/OpenReader.Samples.SamplePlugin.dll `
    samples/SamplePlugin/bin/Release/netstandard2.1/manifest.json `
    $dest/

# 3. Run OpenReader.
dotnet run --project src/ReaderHost.Windows
```

Open Edge and switch tabs — you should hear "tab changed in Edge: ...".

## Scaffold your own

```pwsh
dotnet new install ./templates/openreader-plugin
dotnet new openreader-plugin -n MyShim -o my-shim `
    --PluginId com.acme.my-shim `
    --DisplayName "ACME shim" `
    --TargetExecutable acme.exe
```
