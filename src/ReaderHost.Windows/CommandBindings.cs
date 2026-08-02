using System.Runtime.Versioning;
using Aura.Abstractions.Speech;
using System.Globalization;
using Aura.Config;
using Aura.Core.Diagnostics;
using Aura.Diagnostics;
using Aura.Abstractions.Accessibility;
using Aura.Core.Review;
using Aura.Input.Commands;
using Aura.Input.Gestures;
using Aura.Platform.Windows.Accessibility.Native;
using Aura.Speech;
using Aura.Speech.Punctuation;
using Aura.Speech.Queue;

namespace Aura.Host;

/// <summary>
/// Wires every <see cref="ReaderCommand"/> to its handler on the
/// <see cref="CommandBus"/>. Extracted from <c>Program.cs</c> so the host's
/// boot sequence stays under one screen.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal static class CommandBindings
{
    /// <summary>Cycles through punctuation levels each time CyclePunctuationLevel is invoked.</summary>
    private static PunctuationLevel _punctuationLevel = PunctuationLevel.Some;

    /// <summary>Whether the screen reader is currently announcing anything (toggled by CapsLock double-tap).</summary>
    private static bool _enabled = true;

    public static void Apply(
        CommandBus bus,
        SpeechPipeline pipeline,
        ReviewCursor cursor,
        NativeUiaProvider provider,
        SpeechQueue queue,
        ISpeechEngine engine,
        SettingsHost settingsHost,
        ExitDialogHost exitDialogHost,
        SynthesizerDialogHost synthesizerDialogHost,
        SayAllRunner sayAll,
        DoubleTapDetector doubleTap,
        GestureRouter router,
        Serilog.ILogger log,
        Action<bool> onEnabledChanged,
        Func<ReaderConfig> currentConfig,
        Action<string> copyToClipboard)
    {
        bus.Bind(ReaderCommand.StopSpeech, _ =>
        {
            sayAll.Cancel();
            queue.Clear();
            return engine.CancelAsync();
        });

        bus.Bind(ReaderCommand.OpenSettings, _ =>
        {
            settingsHost.Show();
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.OpenDocumentation, _ =>
        {
            OpenDocumentation(log);
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.OpenExitDialog, _ =>
        {
            exitDialogHost.Show();
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.OpenSynthesizerDialog, _ =>
        {
            synthesizerDialogHost.Show();
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.SayAllFromCursor, _ =>
        {
            sayAll.StartAsync().ContinueWith(_ => { }, TaskScheduler.Default);
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.SayAll, _ =>
        {
            // Snap review to the focused control before reading from the top —
            // otherwise a stale cursor would re-read whatever was last focused.
            if (provider.Focused is { } focusedNode)
            {
                cursor.SyncTo(focusedNode);
            }
            sayAll.StartFromBeginningAsync().ContinueWith(_ => { }, TaskScheduler.Default);
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.ReportDiagnostics, _ =>
        {
            // The reporter cannot read the screen to gather any of this.
            // Without it a bug report is "it stopped talking", and half a day
            // goes on establishing facts that were always available.
            try
            {
                var config = currentConfig();
                var focused = provider.Focused;
                var fields = new List<DiagnosticSnapshot.Field>
                {
                    new("AURA", typeof(CommandBindings).Assembly.GetName().Version?.ToString()),
                    new(".NET", Environment.Version.ToString()),
                    new("OS", Environment.OSVersion.VersionString),
                    new("Architecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()),
                    new("Engine", engine.Id),
                    new("Voice", config.Speech?.VoiceId ?? "(engine default)"),
                    new("Rate", (config.Speech?.RatePercent ?? 100f).ToString(CultureInfo.InvariantCulture) + "%"),
                    new("Layout", config.Keyboard?.Layout),
                    new("Reader modifier", config.Input?.ReaderModifier),
                    new("Punctuation", _punctuationLevel.ToString()),
                    new("Reader enabled", _enabled.ToString()),
                    // Executable name only — never the control's name or value.
                    new("Focused app", DescribeFocusedApp(focused)),
                    new("Focused role", focused?.Role.ToString()),
                    new("Log directory", LogPaths.LogDirectory),
                };

                copyToClipboard(DiagnosticSnapshot.Build("AURA diagnostics", fields));
                Submit(SpeechReason.UserAnnouncement, DiagnosticSnapshot.SpokenSummary(fields.Count));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                log.Warning(ex, "could not build diagnostics snapshot");
                Submit(SpeechReason.UserAnnouncement, "could not copy diagnostics");
            }
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.CyclePunctuationLevel, _ =>
        {
            _punctuationLevel = (PunctuationLevel)(((int)_punctuationLevel + 1) % 4);
            pipeline.PunctuationLevel = _punctuationLevel;
            Submit(SpeechReason.UserAnnouncement, "punctuation " + _punctuationLevel.ToString().ToLowerInvariant());
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.ToggleKeyboardHelp, _ =>
        {
            // GestureRouter flipped its own flag before dispatching; announce the resulting state.
            var msg = router.KeyboardHelpMode
                ? "keyboard help mode on. Press a key to hear what it does. Press Insert+1 again to exit."
                : "keyboard help mode off.";
            Submit(SpeechReason.UserAnnouncement, msg);
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.ToggleEnabled, _ =>
        {
            // Two solo CapsLock taps within ~450 ms toggle the screen reader as a whole.
            // Tray menu and explicit gestures bypass the double-tap gate.
            if (!doubleTap.Observe(0x14 /* VK_CAPITAL */))
            {
                return ValueTask.CompletedTask;
            }
            ToggleReaderEnabled(pipeline, queue, sayAll);
            onEnabledChanged(_enabled);
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.ReadCharacter, _ => { Submit(SpeechReason.ReadCharacter, cursor.ReadCurrentCharacter()); return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.ReadNextCharacter, _ => { Submit(SpeechReason.ReadCharacter, cursor.MoveNextCharacter()); return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.ReadPreviousCharacter, _ => { Submit(SpeechReason.ReadCharacter, cursor.MovePreviousCharacter()); return ValueTask.CompletedTask; });

        bus.Bind(ReaderCommand.ReadWord, _ => { Submit(SpeechReason.ReadWord, cursor.ReadCurrentWord()); return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.ReadNextWord, _ => { Submit(SpeechReason.ReadWord, cursor.MoveNextWord()); return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.ReadPreviousWord, _ => { Submit(SpeechReason.ReadWord, cursor.MovePreviousWord()); return ValueTask.CompletedTask; });

        bus.Bind(ReaderCommand.ReadLine, _ => { Submit(SpeechReason.ReadLine, cursor.ReadCurrentLine()); return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.ReadNextLine, _ => { Submit(SpeechReason.ReadLine, cursor.MoveNextLine()); return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.ReadPreviousLine, _ => { Submit(SpeechReason.ReadLine, cursor.MovePreviousLine()); return ValueTask.CompletedTask; });

        bus.Bind(ReaderCommand.ReviewMoveToFocus, _ =>
        {
            if (provider.Focused is { } n)
            {
                cursor.SyncTo(n);
                Submit(SpeechReason.UserAnnouncement, "review at focus");
            }
            return ValueTask.CompletedTask;
        });
        bus.Bind(ReaderCommand.ReviewMoveToTop, _ =>
        {
            cursor.MoveToStart();
            Submit(SpeechReason.ReadLine, cursor.ReadCurrentLine());
            return ValueTask.CompletedTask;
        });
        bus.Bind(ReaderCommand.ReviewMoveToBottom, _ =>
        {
            cursor.MoveToEnd();
            Submit(SpeechReason.ReadLine, cursor.ReadCurrentLine());
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.ReportFocus, _ =>
        {
            if (provider.Focused is { } n)
            {
                pipeline.Submit(new SpeechRequest(SpeechReason.FocusChanged, n, RawText: null, AppExecutableName: null));
            }
            return ValueTask.CompletedTask;
        });
        bus.Bind(ReaderCommand.ReportTitle, _ =>
        {
            // Walk up to the owning Window — Insert+T should announce the
            // application's window title, not the focused control's name.
            var title = provider.GetFocusedWindowTitle();
            if (string.IsNullOrEmpty(title))
            {
                Submit(SpeechReason.UserAnnouncement, "no title");
            }
            else
            {
                Submit(SpeechReason.UserAnnouncement, title);
            }
            return ValueTask.CompletedTask;
        });
        bus.Bind(ReaderCommand.ReportTime, _ =>
        {
            // Double-tap of the same chord within the window switches to the date.
            if (doubleTap.Observe((int)ReaderCommand.ReportTime))
            {
                Submit(SpeechReason.UserAnnouncement, DateTime.Now.ToLongDateString());
            }
            else
            {
                Submit(SpeechReason.UserAnnouncement, DateTime.Now.ToShortTimeString());
            }
            return ValueTask.CompletedTask;
        });

        bus.Bind(ReaderCommand.ReportDate, _ =>
        {
            Submit(SpeechReason.UserAnnouncement, DateTime.Now.ToLongDateString());
            return ValueTask.CompletedTask;
        });

        return;

        void Submit(SpeechReason reason, string text)
        {
            // Keep the originating reason so blank reads sit at the same
            // priority as a non-blank read of the same kind. Routing empty
            // results through UserAnnouncement (Now-priority) was preempting
            // arbitrary in-flight speech every time the user arrowed past
            // an empty line.
            var effectiveText = string.IsNullOrEmpty(text) ? "blank" : text;
            pipeline.Submit(new SpeechRequest(reason, Node: null, RawText: effectiveText, AppExecutableName: null));
        }
    }

    /// <summary>
    /// The focused application's executable name, from the process id the UIA
    /// mapper already cached. Deliberately just the executable: the control's
    /// name or value would be user content and must not reach a bug report.
    /// </summary>
    private static string? DescribeFocusedApp(AccessibleNode? node)
    {
        if (node is null || !node.Extras.TryGetValue("uia.ProcessId", out var raw) || raw is not int pid)
        {
            return null;
        }
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Process exited between the focus event and the report.
            return null;
        }
    }

    private static void ToggleReaderEnabled(SpeechPipeline pipeline, SpeechQueue queue, SayAllRunner sayAll)
    {
        _enabled = !_enabled;
        sayAll.Cancel();
        queue.Clear();
        pipeline.Submit(new SpeechRequest(SpeechReason.UserAnnouncement, Node: null,
            RawText: _enabled ? "AURA on" : "AURA off", AppExecutableName: null));
    }

    /// <summary>Launch the documentation URL via the OS shell. Public so tray menu can call it directly.</summary>
    public static void OpenDocumentation(Serilog.ILogger log)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Aura/Aura",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            log.Warning(ex, "could not launch documentation");
        }
    }
}
