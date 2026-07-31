namespace OpenReader.Abstractions.Speech;

/// <summary>
/// Why speech was requested. Used by speech rules for scope filtering and by
/// the queue for arbitration policy (e.g., character reads have a different
/// cancel-group policy than focus changes).
/// </summary>
public enum SpeechReason
{
    Unknown = 0,
    FocusChanged,
    ValueChanged,
    SelectionChanged,
    AlertRaised,
    LiveRegionUpdate,
    ReadCharacter,
    ReadWord,
    ReadLine,
    ReadAll,
    ReviewMove,
    /// <summary>
    /// The text caret moved inside an editable / read-only document. Driven by
    /// UIA's <c>TextSelectionChangedEvent</c>; the request's <c>RawText</c>
    /// carries the line under the new caret position.
    /// </summary>
    CaretMoved,
    ScriptInitiated,
    UserAnnouncement,
}
