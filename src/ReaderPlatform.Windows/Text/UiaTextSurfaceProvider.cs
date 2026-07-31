using System.Runtime.Versioning;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Text;
using OpenReader.Diagnostics;
using OpenReader.Platform.Windows.Accessibility;
using OpenReader.Platform.Windows.Interop;
using Serilog;

namespace OpenReader.Platform.Windows.Text;

/// <summary>
/// Picks the best available text backend for a node and hides the choice from
/// everything above.
/// </summary>
/// <remarks>
/// <para>
/// The fallback chain lives here, behind one call. It used to be open-coded in
/// three places — the caret tracker, the text-content provider, and the review
/// cursor — each with a slightly different ordering, and therefore each with
/// its own set of controls it silently failed on.
/// </para>
/// <para>
/// Order matters and is not arbitrary:
/// </para>
/// <list type="number">
///   <item><b>UIA <c>TextPattern</c>.</b> The only backend that knows about
///   layout — wrapped lines, embedded objects, formatting — so it wins wherever
///   it exists.</item>
///   <item><b>Win32 messages.</b> Classic edits answer these when they expose
///   no text pattern at all.</item>
///   <item><b>The node's value, read-only.</b> Better than nothing: a
///   single-line control whose whole value is its only line still supports
///   character and word review this way.</item>
/// </list>
/// <para>
/// <b>Surfaces are cached per node, and that is load-bearing.</b> Ranges are
/// only comparable against ranges from the same surface instance, and caret
/// following compares a previous position against a current one. Handing out a
/// fresh surface per call would make every comparison return "equal" and caret
/// following would silently stop announcing.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class UiaTextSurfaceProvider : ITextSurfaceProvider
{
    private readonly UiaAccessibilityProvider _provider;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private NodeId _cachedId;
    private ITextSurface? _cached;
    private bool _hasCached;

    public UiaTextSurfaceProvider(UiaAccessibilityProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _log = LoggerFactory.ForComponent("UIA.TextSurface");
    }

    public ITextSurface? GetSurface(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        lock (_gate)
        {
            if (_hasCached && _cachedId == node.Id)
            {
                return _cached;
            }
        }

        var surface = Resolve(node);

        lock (_gate)
        {
            // Only one node is ever navigated at a time, so a single slot is
            // the whole cache. Focus moving elsewhere evicts by overwriting.
            _cachedId = node.Id;
            _cached = surface;
            _hasCached = true;
        }
        return surface;
    }

    /// <summary>Drop the cached surface. Call when the focused control's identity may have changed.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _hasCached = false;
            _cached = null;
        }
    }

    private ITextSurface? Resolve(AccessibleNode node)
    {
        var element = _provider.TryGetElement(node.Id);

        // 1. UIA TextPattern. The mapper already recorded availability in the
        //    cached property batch, so this costs nothing to check.
        if (element is not null && Extra(node, "uia.HasTextPattern") is true)
        {
            _log.Verbose("text surface for {NodeId}: UIA TextPattern", node.Id);
            return new UiaTextSurface(element, node.Id);
        }

        // 2. Win32 messages. The window handle also came from the cached batch.
        var hwnd = Extra(node, "uia.NativeWindowHandle") is int h ? (nint)h : 0;
        if (hwnd != 0 && Win32Text.TryGetText(hwnd, out _))
        {
            _log.Verbose("text surface for {NodeId}: Win32 messages", node.Id);
            return new Win32TextSurface(hwnd, node.Id);
        }

        // 3. The node's own value. No caret, but review and say-all still work.
        if (!string.IsNullOrEmpty(node.Value))
        {
            _log.Verbose("text surface for {NodeId}: node value (read-only)", node.Id);
            return new StringTextSurface(node.Value, caretOffset: 0, nodeId: node.Id);
        }

        // A control with no text at all is not a failure; buttons exist.
        return null;
    }

    private static object? Extra(AccessibleNode node, string key)
        => node.Extras.TryGetValue(key, out var raw) ? raw : null;
}
