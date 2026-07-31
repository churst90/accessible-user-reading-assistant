using System.Runtime.Versioning;
using System.Windows.Automation;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Diagnostics;
using OpenReader.Platform.Windows.Interop;
using Serilog;

namespace OpenReader.Platform.Windows.Accessibility;

/// <summary>
/// <see cref="ITextContentProvider"/> over UIA <c>TextPattern</c>. Falls back
/// to <c>ValuePattern</c>, then to a Win32 <c>WM_GETTEXT</c> read against the
/// element's native HWND, then to <see cref="AccessibleNode.Value"/>.
/// </summary>
/// <remarks>
/// Looks up the live <see cref="AutomationElement"/> from
/// <see cref="UiaAccessibilityProvider"/>'s focus cache. Walking the desktop
/// subtree by runtime id on every read would be too slow.
///
/// <para>The Win32 fallback exists because classic Notepad's edit control and
/// many legacy Win32 textboxes don't implement <c>TextPattern</c> or
/// <c>ValuePattern</c> through the MSAA→UIA proxy. Without it, the review
/// cursor ends up with empty text and review/say-all silently no-op in those
/// apps. It goes through <see cref="Win32Text"/>, which is timeout-bounded —
/// a raw <c>SendMessage</c> here would wedge the review path against any
/// window that has stopped pumping.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class UiaTextContentProvider : ITextContentProvider
{
    private const int MaxCharacters = Win32Text.MaxCharacters;

    private readonly UiaAccessibilityProvider _provider;
    private readonly ILogger _log;

    public UiaTextContentProvider(UiaAccessibilityProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _log = LoggerFactory.ForComponent("UIA.TextContent");
    }

    public string? GetText(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var element = _provider.TryGetElement(node.Id);
        if (element is null)
        {
            return node.Value;
        }

        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var rawText) && rawText is TextPattern textPattern)
            {
                var range = textPattern.DocumentRange;
                var text = range.GetText(MaxCharacters);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var rawValue) && rawValue is ValuePattern valuePattern)
            {
                var v = valuePattern.Current.Value;
                if (!string.IsNullOrEmpty(v))
                {
                    return v;
                }
            }

            var hwnd = (nint)element.Current.NativeWindowHandle;
            if (hwnd != 0 && Win32Text.TryGetText(hwnd, out var win32Text))
            {
                return win32Text;
            }
        }
        catch (ElementNotAvailableException ex)
        {
            _log.Verbose(ex, "element gone while reading text content");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Warning(ex, "could not read text content for node {NodeId}", node.Id);
        }

        return node.Value;
    }

}
