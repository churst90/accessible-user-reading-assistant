namespace Aura.Abstractions.Accessibility;

/// <summary>
/// Well-known keys for <see cref="AccessibleNode.Extras"/>.
/// </summary>
/// <remarks>
/// <para>
/// The dictionary is deliberately open — a platform or an app module can put
/// anything in it without a contract change, which is what makes it an escape
/// hatch rather than a second schema. The cost is that a key is only ever a
/// string, so a producer writing <c>NodeExtras.PositionInSet</c> and a consumer
/// reading <c>"uia.PositionInset"</c> agree on nothing and say nothing, with no
/// error anywhere.
/// </para>
/// <para>
/// These are the keys core reads. Anything a platform invents for its own app
/// modules stays a bare string by design; anything the reader itself depends on
/// belongs here, where a typo is a build error.
/// </para>
/// </remarks>
public static class NodeExtras
{
    /// <summary>1-based position within its container, as an <c>int</c>.</summary>
    public const string PositionInSet = "uia.PositionInSet";

    /// <summary>Number of siblings in the container, as an <c>int</c>.</summary>
    public const string SizeOfSet = "uia.SizeOfSet";

    /// <summary>Nesting depth for trees, as an <c>int</c>, 1-based.</summary>
    public const string Level = "uia.Level";

    /// <summary>Owning process id, as an <c>int</c>.</summary>
    public const string ProcessId = "uia.ProcessId";

    /// <summary>Stable per-control identifier assigned by the application.</summary>
    public const string AutomationId = "uia.AutomationId";

    /// <summary>The control's implementation class name.</summary>
    public const string ClassName = "uia.ClassName";

    /// <summary>Which UI framework produced the control — "WPF", "Win32", "XAML".</summary>
    public const string FrameworkId = "uia.FrameworkId";

    /// <summary>Native window handle, as an <c>int</c>, when the control has one.</summary>
    public const string WindowHandle = "uia.WindowHandle";

    /// <summary>Screen rectangle as "left,top,width,height".</summary>
    public const string Bounds = "uia.Bounds";
}
