using System.Text;
using System.Text.RegularExpressions;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Speech;

namespace Aura.Speech.Rules;

/// <summary>
/// Composes a <see cref="SpeechUtterance"/> from a <see cref="SpeechRequest"/>
/// by evaluating an ordered list of <see cref="SpeechRule"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Rules are evaluated in descending priority order. The first matching
/// <see cref="SpeechRuleAction.Emit"/> establishes the utterance text. Subsequent
/// matching <see cref="SpeechRuleAction.Rewrite"/>, <see cref="SpeechRuleAction.Modulate"/>,
/// and <see cref="SpeechRuleAction.SetVoice"/> rules layer over it. A
/// <see cref="SpeechRuleAction.Suppress"/> match aborts and returns <c>null</c>.
/// </para>
/// <para>
/// This class is immutable and safe for concurrent <see cref="Compose"/> calls.
/// </para>
/// </remarks>
public sealed class SpeechRuleEngine
{
    private readonly SpeechRule[] _rules;
    private readonly Dictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);
    private readonly object _regexGate = new();

    public SpeechRuleEngine(IReadOnlyList<SpeechRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.OrderByDescending(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The rules backing this engine, ordered by priority descending.</summary>
    public IReadOnlyList<SpeechRule> Rules => _rules;

    /// <summary>
    /// Compose an utterance from the request, or return <c>null</c> if no rule
    /// produces text (or a suppress rule matches).
    /// </summary>
    public SpeechUtterance? Compose(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = new StringBuilder();
        var prosody = ProsodyHint.Default;
        string? voiceId = null;
        var trace = new List<string>();
        var hasEmit = false;

        foreach (var rule in _rules)
        {
            if (!Matches(rule.Scope, request))
            {
                continue;
            }

            switch (rule.Action)
            {
                case SpeechRuleAction.Emit emit when !hasEmit:
                    text.Append(SpeechTemplate.Render(emit.Template, request));
                    trace.Add(rule.Id);
                    hasEmit = true;
                    break;

                case SpeechRuleAction.Rewrite rewrite when hasEmit:
                    var current = text.ToString();
                    var replaced = GetRegex(rewrite.Pattern).Replace(current, rewrite.Replacement);
                    if (!string.Equals(current, replaced, StringComparison.Ordinal))
                    {
                        text.Clear();
                        text.Append(replaced);
                        trace.Add(rule.Id);
                    }
                    break;

                case SpeechRuleAction.Modulate modulate:
                    prosody = Combine(prosody, modulate.Prosody);
                    trace.Add(rule.Id);
                    break;

                case SpeechRuleAction.SetVoice setVoice:
                    voiceId = setVoice.VoiceId;
                    trace.Add(rule.Id);
                    break;

                case SpeechRuleAction.Suppress:
                    trace.Add(rule.Id);
                    return null;
            }
        }

        if (!hasEmit || text.Length == 0)
        {
            return null;
        }

        var priority = PriorityFor(request.Reason);
        var cancelGroup = CancelGroupFor(request.Reason);

        return new SpeechUtterance(
            Text: text.ToString(),
            Prosody: prosody,
            VoiceId: voiceId,
            Priority: priority,
            CancelGroup: cancelGroup,
            RuleTrace: trace.ToArray());
    }

    private static SpeechPriority PriorityFor(SpeechReason reason) => reason switch
    {
        SpeechReason.AlertRaised or SpeechReason.UserAnnouncement => SpeechPriority.Now,
        SpeechReason.LiveRegionUpdate => SpeechPriority.Background,
        _ => SpeechPriority.Next,
    };

    private static string? CancelGroupFor(SpeechReason reason) => reason switch
    {
        SpeechReason.FocusChanged => "focus",
        SpeechReason.ReviewMove => "review",
        // Arrow-key caret moves should preempt one another so rapid arrow
        // presses feel snappy — same pattern as focus changes.
        SpeechReason.CaretMoved => "caret",
        // Live value updates (sliders, spinners, progress) and selection
        // changes must supersede prior announcements — without a cancel
        // group, rapid arrow-key slider adjustments queue serially and the
        // user hears a stale backlog instead of the current value.
        SpeechReason.ValueChanged => "value",
        SpeechReason.SelectionChanged => "selection",
        _ => null,
    };

    private bool Matches(SpeechRuleScope scope, SpeechRequest request)
    {
        if (scope.Reason is { } reason && reason != request.Reason)
        {
            return false;
        }

        var node = request.Node;

        if (scope.Role is { } role)
        {
            if (node is null || node.Role != role)
            {
                return false;
            }
        }

        if (scope.RequiredStates != AccessibleStates.None)
        {
            if (node is null || (node.States & scope.RequiredStates) != scope.RequiredStates)
            {
                return false;
            }
        }

        if (scope.ForbiddenStates != AccessibleStates.None)
        {
            if (node is not null && (node.States & scope.ForbiddenStates) != 0)
            {
                return false;
            }
        }

        if (scope.AppExecutableName is { } app)
        {
            if (!string.Equals(app, request.AppExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (scope.TextRegex is { } pattern)
        {
            var subject = request.RawText ?? node?.Name ?? string.Empty;
            if (!GetRegex(pattern).IsMatch(subject))
            {
                return false;
            }
        }

        return true;
    }

    private Regex GetRegex(string pattern)
    {
        lock (_regexGate)
        {
            if (!_regexCache.TryGetValue(pattern, out var rx))
            {
                rx = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
                _regexCache[pattern] = rx;
            }
            return rx;
        }
    }

    private static ProsodyHint Combine(ProsodyHint a, ProsodyHint b) => new(
        PitchDelta: a.PitchDelta + b.PitchDelta,
        RatePercent: a.RatePercent * b.RatePercent / 100f,
        VolumeDelta: a.VolumeDelta + b.VolumeDelta);
}
