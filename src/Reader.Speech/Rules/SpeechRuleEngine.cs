using System.Text.RegularExpressions;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;

namespace Aura.Speech.Rules;

/// <summary>
/// Composes a <see cref="Presentation"/> from a <see cref="SpeechRequest"/>
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
    /// Compose a presentation from the request, or return <c>null</c> if no
    /// rule produces anything (or a suppress rule matches).
    /// </summary>
    /// <param name="request">What happened.</param>
    /// <param name="validity">
    /// Whether the announcement will still be worth making by the time it is
    /// spoken. Supplied by the caller because only the caller knows what would
    /// make it stale. <c>null</c> means unconditionally valid.
    /// </param>
    public Presentation? Compose(SpeechRequest request, IValidityPredicate? validity = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<PresentationSegment>? segments = null;
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
                    segments = SpeechTemplate.RenderSegments(emit.Template, request);
                    trace.Add(rule.Id);
                    hasEmit = true;
                    break;

                // A rewrite applies per segment rather than to one joined
                // string. That is not merely tidier: a pattern anchored with ^
                // or $ used to match the whole composed line, so a rule meant
                // to rewrite a control's name could fire on its role instead.
                case SpeechRuleAction.Rewrite rewrite when hasEmit && segments is not null:
                    var rx = GetRegex(rewrite.Pattern);
                    var changed = false;
                    for (var s = 0; s < segments.Count; s++)
                    {
                        var before = segments[s].Text;
                        var after = rx.Replace(before, rewrite.Replacement);
                        if (!string.Equals(before, after, StringComparison.Ordinal))
                        {
                            segments[s] = segments[s] with { Text = after };
                            changed = true;
                        }
                    }
                    if (changed)
                    {
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

        if (!hasEmit || segments is null)
        {
            return null;
        }

        // A rewrite can empty a segment; drop those rather than speaking a gap.
        segments.RemoveAll(static s => s.Kind is not SegmentKind.Cue && s.Text.Length == 0);
        if (segments.Count == 0)
        {
            return null;
        }

        return new Presentation(
            Segments: segments,
            Reason: request.Reason,
            Subject: request.Node?.Id.Value,
            Priority: PriorityFor(request.Reason),
            CancelGroup: CancelGroupFor(request.Reason),
            Validity: validity,
            RuleTrace: trace.ToArray())
        {
            Prosody = prosody,
            VoiceId = voiceId,
        };
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
