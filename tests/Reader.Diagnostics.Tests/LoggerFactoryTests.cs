using FluentAssertions;
using OpenReader.Diagnostics;
using Xunit;

namespace OpenReader.Diagnostics.Tests;

public class LoggerFactoryTests
{
    [Fact]
    public void ForComponent_returns_a_usable_logger()
    {
        var logger = LoggerFactory.ForComponent("TestComponent");

        logger.Should().NotBeNull();
        // Smoke test: log a line. Should not throw.
        logger.Information("hello from {Test}", nameof(LoggerFactoryTests));
    }

    [Fact]
    public void LogPaths_LogDirectory_is_created()
    {
        var dir = LogPaths.LogDirectory;

        Directory.Exists(dir).Should().BeTrue();
        dir.Should().Contain(LogPaths.AppDirectoryName);
    }
}
