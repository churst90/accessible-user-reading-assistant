namespace OpenReader.Abstractions.Plugins;

/// <summary>
/// Snapshot of a process passed to <see cref="IAppModule.Matches"/> when the
/// host evaluates which modules apply to a focused application.
/// </summary>
public sealed record ProcessInfo(
    int ProcessId,
    string ExecutableName,
    string? ExecutablePath,
    string? WindowTitle,
    string? AppUserModelId);
