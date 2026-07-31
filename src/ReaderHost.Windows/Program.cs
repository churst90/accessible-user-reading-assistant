using System.Runtime.Versioning;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Input;
using OpenReader.Abstractions.Speech;
using OpenReader.Config;
using OpenReader.Core.Diagnostics;
using OpenReader.Core.Review;
using OpenReader.Core.Text;
using OpenReader.Diagnostics;
using OpenReader.Input.Commands;
using OpenReader.Input.Echo;
using OpenReader.Input.Gestures;
using OpenReader.Platform.Windows.Accessibility;
using OpenReader.Platform.Windows.Input;
using OpenReader.Platform.Windows.Speech;
using OpenReader.Platform.Windows.Text;
using OpenReader.Platform.Windows.Speech.EspeakNg;
using OpenReader.Scripting;
using OpenReader.Speech;
using OpenReader.Speech.Engines;
using OpenReader.Speech.Queue;
using OpenReader.Speech.Rules;
using Serilog.Events;

namespace OpenReader.Host;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string SingleInstanceMutexName = "Global\\OpenReader.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        // WinExe — no console attached. Skip the console sink (it's a silent
        // no-op anyway, but explicit avoids the wasted call). Logs go to the
        // rotating file sink at %LocalAppData%\OpenReader\logs\.
        LoggerFactory.Configure(minimumLevel: LogEventLevel.Information, console: false);
        var log = LoggerFactory.ForComponent("Host");

        // Last-resort exception handlers. Without these, an unhandled
        // exception on a thread-pool worker, in a click-handler, or in a
        // task continuation tears down the process — the WPF dispatcher's
        // default policy is fatal. We log loudly and keep running where we
        // safely can. The Main-thread try/catch below still owns
        // intentional shutdown.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                log.Fatal(ex, "AppDomain unhandled exception (terminating={Terminating})", e.IsTerminating);
            }
            else
            {
                log.Fatal("AppDomain unhandled non-exception object (terminating={Terminating})", e.IsTerminating);
            }
            LoggerFactory.Shutdown();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error(e.Exception, "unobserved task exception");
            e.SetObserved();
        };

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            log.Warning("another OpenReader instance is already running; exiting");
            return 2;
        }

        try
        {
            return RunAsync(log).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "host crashed");
            return 1;
        }
        finally
        {
            LoggerFactory.Shutdown();
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }

    private static async Task<int> RunAsync(Serilog.ILogger log)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            log.Information("Ctrl+C received; shutting down");
            shutdown.Cancel();
        };
        var token = shutdown.Token;

        log.Information("OpenReader host starting");
        log.Information("Logs at {LogDirectory}", LogPaths.LogDirectory);

        // --- Config ---
        using var configStore = new ConfigStore();
        configStore.AddLayer("defaults", ReaderConfig.Defaults());
        configStore.AddFileLayer("machine", ConfigPaths.MachineConfigPath);
        configStore.AddFileLayer("user", ConfigPaths.UserConfigPath);
        var profileName = configStore.Current.General?.Profile;
        if (IsRealProfile(profileName))
        {
            configStore.AddFileLayer("profile", ConfigPaths.ProfileConfigPath(profileName!));
            log.Information("Active profile: {Profile}", profileName);
        }
        var currentProfile = profileName;
        using var appLayer = new AppLayerSwitcher(configStore);
        ApplyDiagnosticsConfig(configStore.Current);

        // --- Speech ---
        await using var provider = new UiaAccessibilityProvider();
        await using var sapiEngine = new Sapi5Engine();
        // eSpeak NG is opt-in: throws DllNotFoundException when libespeak-ng.dll
        // isn't on the search path (no install). Catch and fall back so the
        // host still runs with SAPI only.
        EspeakNgEngine? espeakEngine = null;
        try
        {
            espeakEngine = new EspeakNgEngine();
        }
        catch (DllNotFoundException)
        {
            log.Information("eSpeak NG not installed; SAPI 5 will be the only available synthesizer");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            log.Warning(ex, "eSpeak NG init failed; falling back to SAPI 5 only");
        }

        var engineRouter = new EngineRouter(initial: sapiEngine);
        using var queue = new SpeechQueue();
        var typingState = new TypingState();
        var baseRules = YamlRuleLoader.LoadDefaults();
        using var pipeline = new SpeechPipeline(
            provider,
            new SpeechRuleEngine(baseRules),
            queue,
            processInfo: null,
            typingState: typingState);
        ApplyEngineSelection(engineRouter, sapiEngine, espeakEngine, configStore.Current.Speech?.Engine, log);
        ApplySpeechConfig(sapiEngine, espeakEngine, pipeline, configStore.Current);
        AutoStartRegistrar.Apply(configStore.Current.General?.StartWithWindows ?? false, log);

        // --- Plugin host ---
        // Hot-reload is enabled when the OPENREADER_DEV env var is set; in
        // production the watcher cost would be wasted (plugins do not change
        // mid-session) and the rescan races could surface flaky behaviour.
        var hotReload = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENREADER_DEV"));
        await using var pluginHost = new PluginHost(
            pluginsRoots: new[]
            {
                // Bundled first-party app modules — present in every install.
                PluginPaths.ShippedAppModulesRoot,
                // User-installed plugins — opt-in.
                PluginPaths.UserPluginsRoot,
            },
            provider: provider,
            announce: pipeline.Submit,
            hotReload: hotReload);
        pluginHost.RulesChanged += () =>
        {
            // Combine static + plugin rules and swap atomically.
            var combined = baseRules.Concat(pluginHost.CurrentRules).ToArray();
            pipeline.UpdateRuleEngine(new SpeechRuleEngine(combined));
        };
        await pluginHost.LoadAllAsync().ConfigureAwait(false);

        // Text model. UiaTextSurfaceProvider owns the whole fallback chain
        // (UIA TextPattern → Win32 messages → the node's value), so nothing
        // above it has to know which backend answered. Review, say-all and
        // caret following all read through it, which is what keeps them
        // agreeing with each other about where a word or line ends.
        var textSurfaces = new UiaTextSurfaceProvider(provider);
        var reviewCursor = new ReviewCursor(textSurfaces);
        var caretTracker = new CaretTracker(
            textSurfaces,
            focusedNode: () => provider.Focused,
            announce: (motion, node) =>
            {
                var request = CaretFollowService.ToRequest(motion, node);
                if (request is not null)
                {
                    pipeline.Submit(request);
                }
            },
            isTyping: () => typingState.IsTyping);
        var focusContext = new FocusContextResolver();
        nint lastTopLevelWindow = 0;
        using var focusBinding = provider.Subscribe(AccessibilityEventKind.FocusChanged, ev =>
        {
            if (ev.Node is { } n)
            {
                // The cached surface belongs to the control we just left.
                textSurfaces.Invalidate();
                reviewCursor.SyncTo(n);
                var exe = focusContext.ResolveExecutableName(n);
                appLayer.SwitchTo(exe);
                _ = pluginHost.OnFocusChangedAsync(FocusContextResolver.BuildProcessInfo(n, exe));

                // Announce the owning top-level window when focus crosses
                // into a different one (e.g. Win+R opening the Run dialog).
                // The chained focus events that follow — Window → ComboBox →
                // Edit — preempt each other via the "focus" cancel-group, so
                // the dialog title would otherwise be dropped before it could
                // play. A Now-priority announcement plays first.
                var element = provider.TryGetElement(n.Id);
                if (element is not null)
                {
                    var (handle, name) = provider.GetTopLevelWindowInfo(element);
                    if (handle != 0 && handle != lastTopLevelWindow && !string.IsNullOrEmpty(name))
                    {
                        lastTopLevelWindow = handle;
                        pipeline.Submit(new SpeechRequest(
                            SpeechReason.UserAnnouncement,
                            Node: null,
                            RawText: name,
                            AppExecutableName: null));
                    }
                    else if (handle != 0)
                    {
                        lastTopLevelWindow = handle;
                    }
                }
            }
        });

        // Refresh the review cursor's text snapshot whenever the focused
        // control's value/text changes — otherwise Reader+arrows reads the
        // text as it was at focus time and ignores subsequent typing.
        // Review loosely follows the system caret, as NVDA does by default:
        // arrowing through text and then pressing a review key should start
        // from where the user actually is.
        using var caretReviewBinding = provider.Subscribe(
            AccessibilityEventKind.CaretMoved,
            _ => reviewCursor.FollowCaret());

        using var valueBinding = provider.Subscribe(
            AccessibilityEventKind.ValueChanged | AccessibilityEventKind.SelectionChanged,
            ev =>
            {
                if (ev.Node is { } n)
                {
                    reviewCursor.Refresh(n);
                }
            });

        // --- Input ---
        var commandBus = new CommandBus();
        var gestureMap = new GestureMap();
        var initialLayout = ResolveLayout(configStore.Current);
        GestureBindings.ApplyDefaults(gestureMap, initialLayout);
        KeyBindingApplier.ApplyOverrides(gestureMap, configStore.Current.Input?.KeyBindings);
        var suppressionPolicy = new InputSuppressionPolicy(gestureMap, initialLayout);

        await using var keyboard = new Win32KeyboardHook();
        keyboard.SetSuppressionPredicate(suppressionPolicy.ShouldSuppress);
        keyboard.SetReaderModifierKey(ResolveReaderModifier(configStore.Current, initialLayout));
        // Set the typing flag synchronously in the hook (before the keystroke
        // reaches the app) so the pipeline reliably mutes the value re-read it
        // triggers — fixes the Run box reading its growing value as you type.
        keyboard.SetTextInputObserver(typingState.NotifyTyping);
        using var router = new GestureRouter(keyboard, gestureMap, commandBus);
        using var caretFollow = new CaretFollowService(keyboard, provider, caretTracker);
        using var keyEcho = new KeyEchoService(
            keyboard,
            text => pipeline.Submit(new SpeechRequest(SpeechReason.UserAnnouncement, Node: null, RawText: text, AppExecutableName: null)),
            ToEchoSettings(configStore.Current),
            onTypingActivity: typingState.NotifyTyping);
        using var lockKeyAnnouncer = new LockKeyAnnouncer(
            keyboard,
            text => pipeline.Submit(new SpeechRequest(SpeechReason.UserAnnouncement, Node: null, RawText: text, AppExecutableName: null)),
            capsLockIsReaderModifier: () =>
            {
                var key = ResolveReaderModifier(configStore.Current, ResolveLayout(configStore.Current));
                return key is Win32KeyboardHook.ReaderModifierKey.CapsLock or Win32KeyboardHook.ReaderModifierKey.Both;
            });

        // --- UI dispatcher (for settings + exit dialogs + tray) ---
        // Runs on a dedicated STA thread with a live message pump. The Main
        // thread is busy draining the speech queue, so it can't pump WPF/WinForms
        // messages — without this thread, BeginInvoke calls into the dispatcher
        // would queue forever and dialogs would never appear.
        using var uiThread = new UiThread();
        var uiDispatcher = uiThread.Dispatcher;
        var settingsHost = new SettingsHost(configStore, engineRouter, uiDispatcher, log);
        var exitDialogHost = new ExitDialogHost(uiDispatcher, shutdown, log);
        // Synthesizer registry is host-owned. SAPI is always present; eSpeak NG
        // appears only when the runtime initialized successfully (i.e. the user
        // has it installed). The dialog persists the user's pick to config; the
        // ConfigStore.Changed handler below swaps the router accordingly.
        var synthesizerDialogHost = new SynthesizerDialogHost(
            configStore,
            uiDispatcher,
            enginesFactory: () =>
            {
                var list = new List<OpenReader.UI.Dialogs.SynthesizerOption>
                {
                    new(sapiEngine.Id, "Microsoft SAPI 5"),
                };
                if (espeakEngine is not null)
                {
                    list.Add(new(espeakEngine.Id, "eSpeak NG"));
                }
                return list;
            },
            announce: text => pipeline.Submit(new SpeechRequest(SpeechReason.UserAnnouncement, Node: null, RawText: text, AppExecutableName: null)),
            log: log);

        var sayAll = new SayAllRunner(reviewCursor, pipeline.Submit);
        var doubleTap = new DoubleTapDetector();

        OpenReader.UI.Tray.TrayIcon? tray = null;
        uiDispatcher.Invoke(() =>
        {
            tray = new OpenReader.UI.Tray.TrayIcon(
                onOpenSettings: () => settingsHost.Show(),
                onOpenDocumentation: () => CommandBindings.OpenDocumentation(log),
                onToggleEnabled: () =>
                {
                    var dispatch = commandBus.DispatchAsync(ReaderCommand.ToggleEnabled);
                    if (!dispatch.IsCompleted)
                    {
                        dispatch.AsTask();
                    }
                },
                onExit: () => exitDialogHost.Show(),
                announce: text => pipeline.Submit(new SpeechRequest(
                    SpeechReason.UserAnnouncement, Node: null, RawText: text, AppExecutableName: null)));
        });

        // Help-mode announcer just speaks the bound command's name.
        router.SetHelpAnnouncer(command =>
            pipeline.Submit(new SpeechRequest(SpeechReason.UserAnnouncement, Node: null,
                RawText: ReaderCommandLabels.Humanize(command), AppExecutableName: null)));

        // CapsLock solo-tap → ToggleEnabled (two taps in quick succession is the user gesture).
        keyboard.SetCapsLockSoloTapHandler(() =>
        {
            var dispatch = commandBus.DispatchAsync(ReaderCommand.ToggleEnabled);
            if (!dispatch.IsCompleted)
            {
                dispatch.AsTask();
            }
        });

        CommandBindings.Apply(commandBus, pipeline, reviewCursor, provider, queue, engineRouter,
            settingsHost, exitDialogHost, synthesizerDialogHost, sayAll, doubleTap, router, log,
            onEnabledChanged: enabled => uiDispatcher.BeginInvoke(() => tray?.SetEnabled(enabled)));

        // --- Live config reaction ---
        configStore.Changed += updated =>
        {
            ApplyDiagnosticsConfig(updated);
            ApplySpeechConfig(sapiEngine, espeakEngine, pipeline, updated);
            ApplyEngineSelection(engineRouter, sapiEngine, espeakEngine, updated.Speech?.Engine, log);
            keyEcho.Update(ToEchoSettings(updated));
            var newLayout = ResolveLayout(updated);
            GestureBindings.Reset(gestureMap, newLayout);
            KeyBindingApplier.ApplyOverrides(gestureMap, updated.Input?.KeyBindings);
            suppressionPolicy.SetLayout(newLayout);
            keyboard.SetReaderModifierKey(ResolveReaderModifier(updated, newLayout));
            AutoStartRegistrar.Apply(updated.General?.StartWithWindows ?? false, log);

            // Profile hot-swap: if the active profile name changed, drop the
            // old "profile" layer and (re)insert one for the new profile.
            // The replacement merges below "app", which is always topmost.
            var newProfile = updated.General?.Profile;
            if (!string.Equals(currentProfile, newProfile, StringComparison.OrdinalIgnoreCase))
            {
                if (configStore.HasLayer("profile"))
                {
                    configStore.RemoveLayer("profile");
                }
                if (IsRealProfile(newProfile))
                {
                    configStore.InsertFileLayer("profile",
                        ConfigPaths.ProfileConfigPath(newProfile!),
                        beforeLayerName: "app");
                    log.Information("Profile switched to {Profile}", newProfile);
                }
                else
                {
                    log.Information("Profile reset to default");
                }
                currentProfile = newProfile;
            }

            log.Information("config reload applied — layout={Layout}, rate={Rate}%", newLayout,
                updated.Speech?.RatePercent ?? 100f);
        };

        // --- Liveness ---
        // Converts a silent hang into an audible one. A blind user given no
        // signal at all cannot tell a frozen app from a dead screen reader
        // from a broken keyboard; the usual recovery is a reboot.
        using var watchdog = new ResponsivenessWatchdog();
        watchdog.Stalled += elapsed =>
        {
            log.Error("reader unresponsive for {Elapsed}ms — input received, no speech produced",
                (int)elapsed.TotalMilliseconds);
            // Deliberately not spoken: speech is the thing that may be wedged.
            SafeBeep(1200, 180, log);
        };
        watchdog.Recovered += () =>
        {
            log.Information("reader responsive again");
            SafeBeep(900, 90, log);
        };
        watchdog.Start();

        queue.PreemptiveEnqueued += _ => FireAndForgetCancel(engineRouter, log);

        // NVDA-style speech interrupt: any non-modifier key-down kills
        // in-flight speech immediately. Pure modifier presses (Shift, Ctrl,
        // Alt, Win, Reader-key) are excluded so users can hold them down
        // without silencing playback. Navigation keys are also excluded —
        // the CaretMoved cancel-group preempts the previous utterance via
        // PreemptiveEnqueued already, and adding a second cancel here just
        // gave SAPI two cancels per keystroke and audible start-up latency.
        keyboard.RawInputReceived += (_, raw) =>
        {
            if (raw.Kind != InputEventKind.KeyDown)
            {
                return;
            }
            if (IsPureModifierKey(raw.KeyCode))
            {
                return;
            }
            if (IsNavigationKey(raw.KeyCode))
            {
                // Navigation still has to produce speech, so the watchdog
                // watches it even though we skip the extra cancel.
                watchdog.NotifyInput();
                return;
            }
            watchdog.NotifyInput();
            FireAndForgetCancel(engineRouter, log);
        };

        pipeline.Start();
        router.Start();
        caretFollow.Start();
        keyEcho.Start();
        lockKeyAnnouncer.Start();
        await provider.StartAsync(token).ConfigureAwait(false);
        await keyboard.StartAsync(token).ConfigureAwait(false);

        log.Information("UIA + SAPI5 + speech pipeline + keyboard hook online");
        log.Information("Config: layout={Layout}, rate={Rate}%, voice={Voice}",
            initialLayout,
            configStore.Current.Speech?.RatePercent ?? 100f,
            configStore.Current.Speech?.VoiceId ?? "(default)");

        // Spoken ready announcement.
        pipeline.Submit(new SpeechRequest(
            SpeechReason.UserAnnouncement,
            Node: null,
            RawText: "OpenReader ready",
            AppExecutableName: null));

        try
        {
            await DrainQueueAsync(queue, engineRouter, log, watchdog.NotifyOutput, token).ConfigureAwait(false);
        }
        finally
        {
            uiDispatcher.Invoke(() => tray?.Dispose());
            if (espeakEngine is not null)
            {
                await espeakEngine.DisposeAsync().ConfigureAwait(false);
            }
        }
        return 0;
    }


    /// <summary>
    /// True if a profile name designates a non-default named profile. The
    /// reserved value <c>"default"</c> means "no profile layer" — the user
    /// layer is used directly.
    /// </summary>
    private static bool IsRealProfile(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !string.Equals(name, "default", StringComparison.OrdinalIgnoreCase);

    private static KeyboardLayout ResolveLayout(ReaderConfig config)
    {
        var raw = config.Keyboard?.Layout ?? "desktop";
        return string.Equals(raw, "laptop", StringComparison.OrdinalIgnoreCase)
            ? KeyboardLayout.Laptop
            : KeyboardLayout.Desktop;
    }

    /// <summary>
    /// Pick the active Reader-modifier key. Honors an explicit
    /// <c>Input.ReaderModifier</c> ("insert" / "capslock" / "both") if the
    /// user set one; otherwise falls back to the layout's natural choice
    /// (Insert on desktop, CapsLock on laptop).
    /// </summary>
    private static Win32KeyboardHook.ReaderModifierKey ResolveReaderModifier(ReaderConfig config, KeyboardLayout layout)
    {
        var raw = config.Input?.ReaderModifier;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw.Trim().ToUpperInvariant() switch
            {
                "INSERT" => Win32KeyboardHook.ReaderModifierKey.Insert,
                "CAPSLOCK" or "CAPS" => Win32KeyboardHook.ReaderModifierKey.CapsLock,
                "BOTH" => Win32KeyboardHook.ReaderModifierKey.Both,
                _ => DefaultForLayout(layout),
            };
        }
        return DefaultForLayout(layout);

        static Win32KeyboardHook.ReaderModifierKey DefaultForLayout(KeyboardLayout l) => l switch
        {
            KeyboardLayout.Laptop => Win32KeyboardHook.ReaderModifierKey.CapsLock,
            _ => Win32KeyboardHook.ReaderModifierKey.Insert,
        };
    }

    /// <summary>
    /// Apply logging preferences. Redaction defaults to <c>on</c> when the
    /// section is absent — a missing config must not silently start writing
    /// the user's spoken text to disk.
    /// </summary>
    private static void ApplyDiagnosticsConfig(ReaderConfig config)
    {
        Redaction.Enabled = config.Diagnostics?.RedactContent ?? true;
        LoggerFactory.SetMinimumLevel(config.Diagnostics?.MinimumLevel);
    }

    private static KeyEchoSettings ToEchoSettings(ReaderConfig config)
    {
        var k = config.Keyboard;
        return new KeyEchoSettings
        {
            SpeakModifiers = k?.SpeakModifiers ?? false,
            SpeakNavigationKeys = k?.SpeakNavigationKeys ?? false,
            SpeakCharacters = k?.SpeakCharacters ?? false,
            SpeakWords = k?.SpeakWords ?? true,
        };
    }

    private static void ApplySpeechConfig(
        Sapi5Engine sapi,
        EspeakNgEngine? espeak,
        SpeechPipeline pipeline,
        ReaderConfig config)
    {
        var speech = config.Speech;
        if (speech is null)
        {
            return;
        }
        var prosody = new ProsodyHint(
            PitchDelta: speech.PitchDelta ?? 0f,
            RatePercent: speech.RatePercent ?? 100f,
            VolumeDelta: speech.VolumeDelta ?? 0f);

        // Voice id is engine-scoped; only apply it to the engine the user
        // currently has selected. Otherwise SAPI5 receives an eSpeak voice
        // name (or vice versa) and silently fails.
        var selectedEngine = speech.Engine ?? "sapi5";
        if (string.Equals(selectedEngine, sapi.Id, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(speech.VoiceId))
        {
            sapi.DefaultVoiceId = speech.VoiceId;
        }
        sapi.DefaultProsody = prosody;

        if (espeak is not null)
        {
            if (string.Equals(selectedEngine, espeak.Id, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(speech.VoiceId))
            {
                espeak.DefaultVoiceId = speech.VoiceId;
            }
            espeak.DefaultProsody = prosody;
        }

        pipeline.CapitalLetterAnnouncement = speech.CapitalLetterAnnouncement ?? "pitch";
    }

    /// <summary>
    /// Set the router to the engine the config asks for (or the closest
    /// available match). When the requested engine isn't loaded — eSpeak NG
    /// not installed but the user wrote "espeak-ng" to config — we keep
    /// SAPI5 selected and log a warning.
    /// </summary>
    private static void ApplyEngineSelection(
        EngineRouter router,
        Sapi5Engine sapi,
        EspeakNgEngine? espeak,
        string? requested,
        Serilog.ILogger log)
    {
        var pick = requested?.ToLowerInvariant() ?? "sapi5";
        ISpeechEngine target = pick switch
        {
            "espeak-ng" when espeak is not null => espeak,
            "espeak-ng" => FallBack(),
            _ => sapi,
        };
        if (router.Id != target.Id)
        {
            router.Switch(target);
        }
        return;

        ISpeechEngine FallBack()
        {
            log.Warning("config requests engine '{Engine}' but it is not available; using SAPI5", pick);
            return sapi;
        }
    }

    /// <summary>
    /// True for vk codes that represent a modifier key on its own. KeyCode
    /// here is already normalized by <see cref="ModifierStateTracker.NormalizeKey"/>,
    /// so VK_LSHIFT/VK_RSHIFT arrive as VK_SHIFT, etc.
    /// </summary>
    private static bool IsPureModifierKey(int vk) => vk switch
    {
        0x10 /* VK_SHIFT   */ => true,
        0x11 /* VK_CONTROL */ => true,
        0x12 /* VK_MENU    */ => true,
        0x14 /* VK_CAPITAL */ => true,
        0x2D /* VK_INSERT  */ => true,
        0x5B /* VK_LWIN    */ => true,
        0x5C /* VK_RWIN    */ => true,
        _ => false,
    };

    /// <summary>
    /// True for cursor-navigation vk codes. CaretFollowService samples these
    /// and the CaretMoved cancel-group preempts in-flight speech already; a
    /// second cancel in parallel gave SAPI two cancels per keystroke and
    /// audible start-up latency.
    /// </summary>
    private static bool IsNavigationKey(int vk) => vk switch
    {
        0x21 /* VK_PRIOR   */ => true,
        0x22 /* VK_NEXT    */ => true,
        0x23 /* VK_END     */ => true,
        0x24 /* VK_HOME    */ => true,
        0x25 /* VK_LEFT    */ => true,
        0x26 /* VK_UP      */ => true,
        0x27 /* VK_RIGHT   */ => true,
        0x28 /* VK_DOWN    */ => true,
        _ => false,
    };

    /// <summary>
    /// Play a bare tone. Used for the stall cue, which must not depend on the
    /// speech pipeline — that is precisely what may be wedged when it fires.
    /// </summary>
    private static void SafeBeep(int frequency, int durationMs, Serilog.ILogger log)
    {
        try
        {
            Console.Beep(frequency, durationMs);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            log.Verbose(ex, "stall cue beep failed");
        }
    }

    private static async void FireAndForgetCancel(EngineRouter engine, Serilog.ILogger log)
    {
        try
        {
            await engine.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            log.Warning(ex, "engine cancel failed");
        }
    }

    private static async Task DrainQueueAsync(SpeechQueue queue, EngineRouter engine, Serilog.ILogger log, Action? onSpoke, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            SpeechUtterance utterance;
            try
            {
                utterance = await queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                queue.SetCurrentSpeakingGroup(utterance.CancelGroup);
                onSpoke?.Invoke();
                await engine.SpeakAsync(utterance, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // utterance preempted; continue
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Redacted: this is Warning level, above the configured
                // minimum, so the raw text reached the log file on disk.
                // Whatever the synthesiser choked on was, by definition,
                // something the user was reading.
                log.Warning(ex, "speech engine threw on utterance {Text}", Redaction.Text(utterance.Text));
            }
            finally
            {
                queue.SetCurrentSpeakingGroup(null);
            }
        }
    }
}
