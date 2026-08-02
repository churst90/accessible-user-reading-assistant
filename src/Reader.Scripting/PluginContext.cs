using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;
using Aura.Abstractions.Speech;

namespace Aura.Scripting;

/// <summary>
/// Per-attached-plugin implementation of <see cref="IAppContext"/>. Owns the
/// list of <see cref="SpeechRule"/>s the plugin has registered so they can be
/// dropped on detach.
/// </summary>
/// <remarks>
/// The context is recreated on every attach; rules registered through it are
/// scoped to that lifetime. The host is notified each time the rule list
/// changes so the speech pipeline can rebuild its rule engine.
/// </remarks>
internal sealed class PluginContext : IAppContext, IDisposable
{
    private readonly IAccessibilityProvider _provider;
    private readonly Func<SpeechRequest, bool> _announce;
    private readonly Action _onRulesChanged;
    private readonly List<SpeechRule> _rules = new();
    private readonly object _gate = new();
    private bool _disposed;

    public PluginContext(
        ProcessInfo process,
        IAccessibilityProvider provider,
        Func<SpeechRequest, bool> announce,
        Action onRulesChanged)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _announce = announce ?? throw new ArgumentNullException(nameof(announce));
        _onRulesChanged = onRulesChanged ?? throw new ArgumentNullException(nameof(onRulesChanged));
    }

    public ProcessInfo Process { get; }

    public IAccessibilityProvider Accessibility => _provider;

    /// <summary>Snapshot of currently-registered rules; safe to read concurrently.</summary>
    public IReadOnlyList<SpeechRule> Rules
    {
        get
        {
            lock (_gate)
            {
                return _rules.ToArray();
            }
        }
    }

    public ValueTask AnnounceAsync(string text, SpeechPriority priority = SpeechPriority.Next, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text) || _disposed)
        {
            return ValueTask.CompletedTask;
        }
        var request = new SpeechRequest(
            Reason: SpeechReason.UserAnnouncement,
            Node: null,
            RawText: text,
            AppExecutableName: Process.ExecutableName);
        _announce(request);
        return ValueTask.CompletedTask;
    }

    public IDisposable RegisterSpeechRule(SpeechRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (_disposed)
        {
            return EmptyDisposable.Instance;
        }
        lock (_gate)
        {
            _rules.Add(rule);
        }
        _onRulesChanged();
        return new RuleHandle(this, rule);
    }

    public void Dispose()
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            changed = _rules.Count > 0;
            _rules.Clear();
        }
        if (changed)
        {
            _onRulesChanged();
        }
    }

    private void Unregister(SpeechRule rule)
    {
        bool removed;
        lock (_gate)
        {
            removed = _rules.Remove(rule);
        }
        if (removed)
        {
            _onRulesChanged();
        }
    }

    private sealed class RuleHandle : IDisposable
    {
        private readonly PluginContext _owner;
        private readonly SpeechRule _rule;
        private bool _disposed;

        public RuleHandle(PluginContext owner, SpeechRule rule)
        {
            _owner = owner;
            _rule = rule;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner.Unregister(_rule);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
