using FluentAssertions;
using OpenReader.Diagnostics;
using Xunit;

namespace OpenReader.Diagnostics.Tests;

public sealed class RedactionTests : IDisposable
{
    private readonly bool _original = Redaction.Enabled;

    public void Dispose() => Redaction.Enabled = _original;

    [Fact]
    public void Redaction_is_on_by_default()
    {
        // The default for a privacy control is the safe one. If this test ever
        // fails, spoken text is reaching the log file.
        Redaction.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Enabled_replaces_content_with_its_shape()
    {
        Redaction.Enabled = true;
        var result = Redaction.Text("hunter2 is my banking password");

        result.Should().NotContain("hunter2");
        result.Should().NotContain("banking");
        result.Should().Be("(redacted, 30 chars)");
    }

    [Fact]
    public void A_single_character_leaks_nothing()
    {
        // A screen reader announces single characters constantly. This is the
        // case a hash-based token would fail: a digest of "a" is reversible by
        // anyone with a keyboard.
        Redaction.Enabled = true;
        Redaction.Text("a").Should().Be("(redacted, 1 chars)");
        Redaction.Text("b").Should().Be("(redacted, 1 chars)");
        Redaction.Text("a").Should().Be(Redaction.Text("z"));
    }

    [Fact]
    public void Disabled_passes_content_through()
    {
        Redaction.Enabled = false;
        Redaction.Text("hello").Should().Be("hello");
    }

    [Fact]
    public void Null_and_empty_are_distinguishable_without_leaking()
    {
        Redaction.Enabled = true;
        Redaction.Text(null).Should().Be("(null)");
        Redaction.Text(string.Empty).Should().Be("(empty)");
    }
}
