using FluentAssertions;
using Aura.Scripting;
using Xunit;

namespace Aura.Scripting.Tests;

public class PluginManifestFileTests
{
    [Fact]
    public void FromJson_parses_all_fields()
    {
        const string json = """
            {
              "id": "com.example.plug",
              "displayName": "Example",
              "version": "1.2.3",
              "apiVersion": "1.0",
              "assembly": "Example.dll",
              "moduleType": "Example.Module",
              "author": "ACME",
              "description": "test plugin"
            }
            """;
        var m = PluginManifestFile.FromJson(json);
        m.Id.Should().Be("com.example.plug");
        m.DisplayName.Should().Be("Example");
        m.Version.Should().Be("1.2.3");
        m.ApiVersion.Should().Be("1.0");
        m.Assembly.Should().Be("Example.dll");
        m.ModuleType.Should().Be("Example.Module");
        m.Author.Should().Be("ACME");
        m.Description.Should().Be("test plugin");
    }

    [Fact]
    public void TryLoad_rejects_missing_required_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), "aura-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{ "id": "x" }""");
        try
        {
            var manifest = PluginManifestFile.TryLoad(path, out var error);
            manifest.Should().BeNull();
            error.Should().NotBeNull();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryLoad_rejects_unparseable_versions()
    {
        var path = Path.Combine(Path.GetTempPath(), "aura-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
            { "id": "x", "assembly": "x.dll", "moduleType": "X.M",
              "version": "not-a-version", "apiVersion": "1.0" }
            """);
        try
        {
            PluginManifestFile.TryLoad(path, out var error).Should().BeNull();
            error.Should().Contain("Version");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ToManifest_round_trips_to_AppModuleManifest()
    {
        var file = PluginManifestFile.FromJson("""
            { "id": "x", "displayName": "X", "version": "1.0.0",
              "apiVersion": "1.0", "assembly": "x.dll", "moduleType": "X.M" }
            """);
        var m = file.ToManifest();
        m.Id.Should().Be("x");
        m.Version.Should().Be(new System.Version(1, 0, 0));
        m.ApiVersion.Should().Be(new System.Version(1, 0));
    }
}
