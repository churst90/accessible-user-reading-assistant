# Aura.Sdk

The plugin contract for [Aura](https://github.com/Aura/Aura),
an open-source Windows screen reader.

Install:

```pwsh
dotnet add package Aura.Sdk
```

Implement an app module:

```csharp
using Aura.Abstractions.Plugins;

public sealed class MyShim : IAppModule
{
    public AppModuleManifest Manifest { get; } = new(
        Id: "com.example.my-shim",
        DisplayName: "My shim",
        Version: new Version(1, 0, 0),
        ApiVersion: new Version(1, 0));

    public bool Matches(ProcessInfo process)
        => string.Equals(process.ExecutableName, "myapp.exe", StringComparison.OrdinalIgnoreCase);

    public ValueTask OnAttachAsync(IAppContext context, CancellationToken ct)
    {
        return context.AnnounceAsync("my shim attached");
    }

    public ValueTask OnDetachAsync(CancellationToken ct) => default;
}
```

Ship a `manifest.json` next to your DLL:

```json
{
  "id": "com.example.my-shim",
  "displayName": "My shim",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "assembly": "MyShim.dll",
  "moduleType": "MyShim.MyShim"
}
```

Drop the folder under `%AppData%\Aura\plugins\my-shim\` and Aura
will load it on next start (or instantly if `AURA_DEV` is set).

## Compatibility

The host loads modules whose declared `apiVersion` has the same major as
the host's `PluginApi.CurrentApiVersion` and a minor `<=` the host's. New
contract members → minor bump. Removed/renamed members → major bump.

This NuGet package's version tracks Aura's. The plugin API version
is independent and changes only when the contract surface changes.

## License

MIT.
