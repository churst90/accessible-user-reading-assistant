using Aura.Abstractions.Navigation;

namespace Aura.Input.Gestures;

/// <summary>
/// The situation a keystroke arrives in. Decides which gesture layers apply.
/// </summary>
/// <remarks>
/// <para>
/// A flat global key map cannot express Read mode. Quick navigation needs
/// <c>h</c> to mean "next heading" — but only while reading a document, and
/// never while the user is typing their name into a form. Binding <c>h</c>
/// globally would break typing everywhere; not binding it makes Read mode
/// useless. The missing concept is not a binding, it is the <em>context</em> a
/// binding is valid in.
/// </para>
/// <para>
/// Same shape for the other cases already on the roadmap: an app module that
/// wants a chord only inside its application, a plugin command that only makes
/// sense with a braille display attached.
/// </para>
/// </remarks>
public readonly record struct GestureContext(
    ReaderMode Mode = ReaderMode.Type,
    string? AppExecutableName = null,
    bool HasReadModeBuffer = false)
{
    /// <summary>
    /// The context when nothing is known — a plain application with no
    /// document. Deliberately <see cref="ReaderMode.Type"/>: assuming Read
    /// mode would swallow the user's keystrokes, and swallowing input is a far
    /// worse failure than missing a shortcut.
    /// </summary>
    public static GestureContext Default => new();
}
