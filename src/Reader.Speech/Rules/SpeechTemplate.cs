using System.Text;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Speech;

namespace Aura.Speech.Rules;

/// <summary>
/// Token substitution for <see cref="SpeechRuleAction.Emit"/> templates.
/// Tokens recognized: <c>{name}</c>, <c>{value}</c>, <c>{role}</c>,
/// <c>{description}</c>, <c>{text}</c>, <c>{position}</c>, <c>{setSize}</c>,
/// <c>{level}</c>, <c>{posInSet}</c>. Unknown tokens are left untouched.
/// </summary>
internal static class SpeechTemplate
{
    public static string Render(string template, SpeechRequest request)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var node = request.Node;
        var sb = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c != '{')
            {
                sb.Append(c);
                i++;
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            var token = template.AsSpan(i + 1, close - i - 1);
            string? replacement = token switch
            {
                _ when token.SequenceEqual("name") => node?.Name,
                _ when token.SequenceEqual("value") => node?.Value,
                _ when token.SequenceEqual("description") => node?.Description,
                _ when token.SequenceEqual("role") => FormatRole(node?.Role),
                _ when token.SequenceEqual("text") => request.RawText,
                _ when token.SequenceEqual("position") => GetExtraInt(node, "uia.PositionInSet"),
                _ when token.SequenceEqual("setSize") => GetExtraInt(node, "uia.SizeOfSet"),
                _ when token.SequenceEqual("level") => GetExtraInt(node, "uia.Level"),
                _ when token.SequenceEqual("posInSet") => FormatPosInSet(node),
                _ => null,
            };

            if (replacement is null && IsKnownToken(token))
            {
                replacement = string.Empty;
            }

            if (replacement is null)
            {
                sb.Append(template, i, close - i + 1);
            }
            else
            {
                sb.Append(replacement);
            }

            i = close + 1;
        }

        return Tidy(sb);
    }

    private static bool IsKnownToken(ReadOnlySpan<char> token) =>
        token.SequenceEqual("name")
        || token.SequenceEqual("value")
        || token.SequenceEqual("description")
        || token.SequenceEqual("role")
        || token.SequenceEqual("text")
        || token.SequenceEqual("position")
        || token.SequenceEqual("setSize")
        || token.SequenceEqual("level")
        || token.SequenceEqual("posInSet");

    private static string? GetExtraInt(AccessibleNode? node, string key)
    {
        if (node is null || !node.Extras.TryGetValue(key, out var raw) || raw is not int v)
        {
            return null;
        }
        return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? FormatPosInSet(AccessibleNode? node)
    {
        if (node is null)
        {
            return null;
        }
        if (!node.Extras.TryGetValue("uia.PositionInSet", out var p) || p is not int pos)
        {
            return null;
        }
        if (!node.Extras.TryGetValue("uia.SizeOfSet", out var s) || s is not int size)
        {
            return null;
        }
        return $"{pos.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {size.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
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

    /// <summary>
    /// Collapse the artifacts of empty-token expansion: doubled separators, trailing
    /// separator after a missing field. Keeps emitted speech natural-sounding when a
    /// node has no value/name.
    /// </summary>
    private static string Tidy(StringBuilder sb)
    {
        var s = sb.ToString();
        // ", , " → ", "
        while (s.Contains(", , ", StringComparison.Ordinal))
        {
            s = s.Replace(", , ", ", ", StringComparison.Ordinal);
        }
        // trailing ", " or leading ", "
        s = s.TrimEnd().TrimEnd(',').TrimEnd();
        if (s.StartsWith(", ", StringComparison.Ordinal))
        {
            s = s[2..];
        }
        return s;
    }
}
