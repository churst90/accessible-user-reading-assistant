namespace Aura.Abstractions.Navigation;

/// <summary>
/// Whether keystrokes navigate the document or go to the application.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>Read</c> and <c>Write</c> rather than the traditional "browse" and
/// "focus". The old names describe the screen reader's internal state;
/// these describe what the user is doing, which is the only thing they should
/// have to reason about. "Am I reading or am I writing" needs no explanation;
/// "am I in browse mode or focus mode" has needed one for twenty years.
/// </para>
/// <para>
/// <c>Write</c> rather than <c>Type</c> because the pair has to read as a pair.
/// Read/Write is a distinction every computer user already holds, it carries
/// the right meaning for the cases that are not literally typing — pressing a
/// button, checking a box, dragging a slider — and it does not collide with
/// "type" the programming word in a codebase full of them.
/// </para>
/// </remarks>
public enum ReaderMode
{
    /// <summary>
    /// Keystrokes go to the application. What the user types is entered;
    /// reader commands still work through the reader modifier.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately zero.</b> A default-initialised struct holding a
    /// <see cref="ReaderMode"/> gets this value, and the two modes fail very
    /// differently: defaulting to <see cref="Read"/> means a
    /// zero-initialised context silently swallows the user's typing, with no
    /// error and nothing on screen to explain it. Defaulting to
    /// <see cref="Write"/> means at worst a shortcut does not fire. The
    /// recoverable failure gets the zero.
    /// </remarks>
    Write = 0,

    /// <summary>
    /// Keystrokes navigate. Arrows move through the document by line and
    /// character, single letters jump between elements (<c>h</c> for the next
    /// heading, <c>k</c> for the next link), and the application never sees
    /// them.
    /// </summary>
    Read = 1,
}
