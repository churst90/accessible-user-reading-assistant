# First Steps

The exact ordered checklist for the next coding session. Goal: end of Phase 0
in this file's terms = solution scaffolding compiles and a synthetic-tree
unit test passes.

## Prerequisites to verify

- [ ] .NET 8 SDK installed (`dotnet --version` ≥ 8.0).
- [ ] Windows SDK present (for `net8.0-windows` TFM).
- [ ] Git initialized at `Aura/` root (`git init`).

## Step 1 — Solution + Directory.Build.props

Create at `Aura/`:

- `Aura.sln`
- `Directory.Build.props` enforcing globally:
  - `<Nullable>enable</Nullable>`
  - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  - `<LangVersion>latest</LangVersion>`
  - `<AnalysisLevel>latest-recommended</AnalysisLevel>`
- `.editorconfig` (4-space indent, `file_scoped_namespace = true`).
- `.gitignore` (.NET template).

## Step 2 — Project skeletons (no logic)

Create empty projects under `src/` matching the directory layout:

```
dotnet new classlib -n Reader.Abstractions   -f netstandard2.1
dotnet new classlib -n Reader.Core           -f net8.0
dotnet new classlib -n Reader.Speech         -f net8.0
dotnet new classlib -n Reader.Output         -f net8.0
dotnet new classlib -n Reader.Input          -f net8.0
dotnet new classlib -n Reader.Scripting      -f net8.0
dotnet new classlib -n Reader.Config         -f net8.0
dotnet new classlib -n Reader.Diagnostics    -f net8.0
dotnet new classlib -n ReaderPlatform.Windows -f net8.0-windows
dotnet new console  -n ReaderHost.Windows    -f net8.0-windows
```

Project references (top-down):
- `ReaderHost.Windows` → `ReaderPlatform.Windows`, `Reader.Core`,
  `Reader.Speech`, `Reader.Output`, `Reader.Input`, `Reader.Scripting`,
  `Reader.Config`, `Reader.Diagnostics`
- `ReaderPlatform.Windows` → `Reader.Abstractions`, `Reader.Diagnostics`
- `Reader.Core` → `Reader.Abstractions`, `Reader.Diagnostics`
- `Reader.Speech` / `Output` / `Input` / `Scripting` / `Config` →
  `Reader.Abstractions`, `Reader.Diagnostics`
- `Reader.Diagnostics` → (none — it's the bottom)
- `Reader.Abstractions` → (none — it's the contract)

Add all projects to `Aura.sln`.

## Step 3 — Define the abstractions

In `Reader.Abstractions`, write the interfaces and types from
`ARCHITECTURE.md` § Key Abstractions. Pure declarations, no implementations.
This is the contract everything else implements.

Files:
- `Accessibility/IAccessibilityProvider.cs`
- `Accessibility/AccessibleNode.cs`
- `Accessibility/AccessibleRole.cs`
- `Accessibility/AccessibleStates.cs`
- `Accessibility/AccessibilityEvent.cs`
- `Speech/ISpeechEngine.cs`
- `Speech/SpeechUtterance.cs`
- `Speech/SpeechRequest.cs`
- `Speech/SpeechRule.cs`
- `Input/IInputSource.cs`
- `Input/RawInput.cs`
- `Plugins/IAppModule.cs`
- `Plugins/AppModuleManifest.cs`

## Step 4 — Wire diagnostics

Add Serilog to `Reader.Diagnostics`. Expose:
- `LoggerFactory.CreateForComponent(string name)`
- Default rotating file sink at `%LocalAppData%\Aura\logs\`.
- A minimal `Activity`-based perf counter wrapper for hot-path timing.

## Step 5 — Test harness

Create `tests/` projects mirroring `src/`:
```
dotnet new xunit -n Reader.Abstractions.Tests
dotnet new xunit -n Reader.Core.Tests
dotnet new xunit -n Reader.Speech.Tests
... etc
```

Build a `SyntheticAccessibilityProvider` in `tests/Reader.TestKit/`:

```csharp
var tree = new SyntheticTreeBuilder()
    .Window("Notepad", n => n
        .MenuBar(m => m
            .Menu("File", f => f
                .MenuItem("Open"))))
    .Build();

var provider = new SyntheticAccessibilityProvider(tree);
provider.SimulateFocus("Open");
```

This is the foundation that lets every upstream test run without launching a
real app.

## Step 6 — First end-to-end test

In `Reader.Speech.Tests`, write the canonical happy-path test:

```
GIVEN a synthetic provider with a Button labeled "OK"
WHEN focus moves to the button
THEN the SpeechQueue receives an utterance whose text contains "OK button"
```

This test will fail until Phase 1 is built — that's fine. It's our contract.
Mark it `[Fact(Skip = "Phase 1")]` for now.

## Step 7 — CI

`.github/workflows/ci.yml`:
```yaml
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -warnaserror
      - run: dotnet test --no-build
```

## Done when

- `dotnet build` succeeds with zero warnings.
- `dotnet test` runs and reports >0 tests passing.
- CI badge is green.
- Pushing the skeleton commits the design docs alongside it.

After this, Phase 1 begins: the first real UIA subscription and the first
SAPI5 utterance. That work is described in `ROADMAP.md`.
