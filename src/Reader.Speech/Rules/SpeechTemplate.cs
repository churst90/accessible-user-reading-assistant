using System.Globalization;
using System.Text;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;

namespace Aura.Speech.Rules;

/// <summary>
/// Turns an <see cref="SpeechRuleAction.Emit"/> template into
/// <see cref="PresentationSegment"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Tokens recognized: <c>{name}</c>, <c>{value}</c>, <c>{role}</c>,
/// <c>{description}</c>, <c>{text}</c>, <c>{position}</c>, <c>{setSize}</c>,
/// <c>{level}</c>, <c>{posInSet}</c>. Unknown tokens are left untouched, which
/// makes a typo audible rather than silent.
/// </para>
/// <para>
/// <b>Segmentation rule.</b> A template is split at literal <c>", "</c>
/// boundaries; everything between two boundaries is one segment. That keeps
/// <c>"level {level}"</c> together as one segment reading "level 2" while
/// <c>"{name}, button"</c> becomes two. The segment's kind comes from the token
/// it contains, or <see cref="SegmentKind.Literal"/> when it contains none.
/// </para>
/// <para>
/// <b>A segment whose token renders empty is dropped whole</b> — a label
/// without its value ("level ,") is noise. Doing that structurally is what
/// replaces the old string-tidying pass, which was approximating the same
/// result by collapsing doubled separators and trimming trailing commas after
/// the damage was done.
/// </para>
/// <para>
/// A template may declare a kind explicitly with <c>{kind:words}</c>, e.g.
/// <c>{state:checked}</c> or <c>{hint:press space to activate}</c>. Nothing
/// ships using it yet; it is the migration path for turning today's literal
/// role and state words into filterable kinds without a contract change.
/// </para>
/// </remarks>
internal static class SpeechTemplate
{
    /// <summary>
    /// The separator a speech renderer joins segments with.
    /// </summary>
    /// <remarks>
    /// Comma and space, because that is what the templates were written
    /// against. NVDA joins with two spaces instead, deliberately: a comma
    /// changes number reading in French and German, where space is a thousands
    /// separator. Worth revisiting when F7 lands locale data.
    /// </remarks>
    public const string JoinSeparator = ", ";

    /// <summary>Render to segments. Never returns null; may return empty.</summary>
    public static List<PresentationSegment> RenderSegments(string template, SpeechRequest request)
    {
        var segments = new List<PresentationSegment>(4);
        if (string.IsNullOrEmpty(template))
        {
            return segments;
        }

        var node = request.Node;
        var text = new StringBuilder();
        var kind = SegmentKind.Literal;
        var dropSegment = false;
        var sawToken = false;

        void Flush()
        {
            if (!dropSegment)
            {
                var s = text.ToString().Trim();
                if (s.Length > 0)
                {
                    // A literal that IS this node's role word is a role, not a
                    // literal. Templates say "{name}, list item" rather than
                    // "{name}, {role}", because the role word is often more
                    // specific than the enum ("check box, checked"), and
                    // nothing in the string says which part is the role.
                    // Matching it against the formatted role recovers that for
                    // free, so verbosity can drop role words without every
                    // template being rewritten first.
                    var effective = kind == SegmentKind.Literal && IsRoleWord(s, node?.Role)
                        ? SegmentKind.Role
                        : kind;
                    segments.Add(new PresentationSegment(s, effective, node?.Role));
                }
            }
            text.Clear();
            kind = SegmentKind.Literal;
            dropSegment = false;
            sawToken = false;
        }

        var i = 0;
        while (i < template.Length)
        {
            // A literal ", " ends the current segment.
            if (template[i] == ',' && i + 1 < template.Length && template[i + 1] == ' ')
            {
                Flush();
                i += 2;
                continue;
            }

            if (template[i] != '{')
            {
                text.Append(template[i]);
                i++;
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                // Unterminated brace: emit the rest verbatim so the mistake is heard.
                text.Append(template, i, template.Length - i);
                break;
            }

            var token = template.AsSpan(i + 1, close - i - 1);
            var resolved = Resolve(token, request, node, out var tokenKind);

            if (resolved is null)
            {
                // Unknown token — leave it in place, typo included.
                text.Append(template, i, close - i + 1);
            }
            else
            {
                if (resolved.Length == 0)
                {
                    dropSegment = true;
                }
                text.Append(resolved);
                // The first token in a segment names it; a second leaves the
                // kind alone rather than fighting over it.
                if (!sawToken)
                {
                    kind = tokenKind;
                    sawToken = true;
                }
            }

            i = close + 1;
        }

        Flush();
        return segments;
    }

    /// <summary>
    /// Resolve a token: <c>null</c> for an unknown token, empty for a known
    /// token with no value.
    /// </summary>
    private static string? Resolve(
        ReadOnlySpan<char> token,
        SpeechRequest request,
        AccessibleNode? node,
        out SegmentKind kind)
    {
        // Explicit kind: {state:checked}
        var colon = token.IndexOf(':');
        if (colon > 0 && TryParseKind(token[..colon], out kind))
        {
            return token[(colon + 1)..].ToString();
        }

        if (token.SequenceEqual("name"))
        {
            kind = SegmentKind.Name;
            return node?.Name ?? string.Empty;
        }
        if (token.SequenceEqual("value"))
        {
            kind = SegmentKind.Value;
            return node?.Value ?? string.Empty;
        }
        if (token.SequenceEqual("description"))
        {
            kind = SegmentKind.Description;
            return node?.Description ?? string.Empty;
        }
        if (token.SequenceEqual("role"))
        {
            kind = SegmentKind.Role;
            return FormatRole(node?.Role);
        }
        if (token.SequenceEqual("text"))
        {
            kind = SegmentKind.Content;
            return request.RawText ?? string.Empty;
        }
        if (token.SequenceEqual("position"))
        {
            kind = SegmentKind.Position;
            return GetExtraInt(node, NodeExtras.PositionInSet) ?? string.Empty;
        }
        if (token.SequenceEqual("setSize"))
        {
            kind = SegmentKind.Position;
            return GetExtraInt(node, NodeExtras.SizeOfSet) ?? string.Empty;
        }
        if (token.SequenceEqual("level"))
        {
            kind = SegmentKind.Position;
            return GetExtraInt(node, NodeExtras.Level) ?? string.Empty;
        }
        if (token.SequenceEqual("posInSet"))
        {
            kind = SegmentKind.Position;
            return FormatPosInSet(node) ?? string.Empty;
        }

        kind = SegmentKind.Literal;
        return null;
    }

    private static bool IsRoleWord(string text, AccessibleRole? role)
        => role is not null && string.Equals(text, FormatRole(role), StringComparison.Ordinal);

    private static bool TryParseKind(ReadOnlySpan<char> declared, out SegmentKind kind)
    {
        if (declared.SequenceEqual("state")) { kind = SegmentKind.State; return true; }
        if (declared.SequenceEqual("role")) { kind = SegmentKind.Role; return true; }
        if (declared.SequenceEqual("hint")) { kind = SegmentKind.Hint; return true; }
        if (declared.SequenceEqual("position")) { kind = SegmentKind.Position; return true; }
        if (declared.SequenceEqual("structure")) { kind = SegmentKind.Structure; return true; }
        if (declared.SequenceEqual("cue")) { kind = SegmentKind.Cue; return true; }
        kind = SegmentKind.Literal;
        return false;
    }

    private static string? GetExtraInt(AccessibleNode? node, string key)
    {
        if (node is null || !node.Extras.TryGetValue(key, out var raw) || raw is not int v)
        {
            return null;
        }
        return v.ToString(CultureInfo.InvariantCulture);
    }

    private static string? FormatPosInSet(AccessibleNode? node)
    {
        if (node is null
            || !node.Extras.TryGetValue(NodeExtras.PositionInSet, out var p) || p is not int pos
            || !node.Extras.TryGetValue(NodeExtras.SizeOfSet, out var s) || s is not int size)
        {
            return null;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{pos} of {size}");
    }

    /// <summary>Convert PascalCase role to lower-case spaced ("MenuItem" → "menu item").</summary>
    private static string FormatRole(AccessibleRole? role)
    {
        if (role is null || role == AccessibleRole.Unknown)
        {
            return string.Empty;
        }

        var name = role.Value.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (i > 0 && char.IsUpper(ch))
            {
                sb.Append(' ');
            }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
